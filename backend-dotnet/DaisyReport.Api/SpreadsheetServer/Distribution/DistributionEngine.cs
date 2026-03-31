using System.Diagnostics;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.SpreadsheetServer.Distribution.Channels;
using DaisyReport.Api.SpreadsheetServer.Distribution.Models;
using DaisyReport.Api.SpreadsheetServer.Distribution.Renderers;
using DaisyReport.Api.SpreadsheetServer.Services;

namespace DaisyReport.Api.SpreadsheetServer.Distribution;

public interface IDistributionEngine
{
    Task<DistributionResult> ExecuteAsync(DistributionConfig config, CancellationToken ct = default);
    Task<DistributionResult> PreviewAsync(DistributionConfig config, CancellationToken ct = default);
}

public class DistributionEngine : IDistributionEngine
{
    private readonly IDatabase _database;
    private readonly ISpreadsheetQueryService _queryService;
    private readonly IAuditRepository _auditRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DistributionEngine> _logger;

    public DistributionEngine(
        IDatabase database,
        ISpreadsheetQueryService queryService,
        IAuditRepository auditRepository,
        IServiceProvider serviceProvider,
        ILogger<DistributionEngine> logger)
    {
        _database = database;
        _queryService = queryService;
        _auditRepository = auditRepository;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<DistributionResult> ExecuteAsync(DistributionConfig config, CancellationToken ct = default)
    {
        return await RunAsync(config, deliver: true, ct);
    }

    public async Task<DistributionResult> PreviewAsync(DistributionConfig config, CancellationToken ct = default)
    {
        return await RunAsync(config, deliver: false, ct);
    }

    private async Task<DistributionResult> RunAsync(DistributionConfig config, bool deliver, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = new DistributionResult();

        try
        {
            // 1. Load the template
            var template = await LoadTemplateAsync(config.TemplateId);
            if (template == null)
            {
                result.Errors.Add($"Template {config.TemplateId} not found.");
                result.DurationMs = sw.ElapsedMilliseconds;
                return result;
            }

            // 2. Resolve burst values
            var burstValues = await ResolveBurstValuesAsync(config, ct);

            // 3. Build renderers
            var resolver = new FormulaResolver(
                _queryService,
                _serviceProvider.GetRequiredService<ILogger<FormulaResolver>>());
            var excelRenderer = new ExcelRenderer(
                resolver,
                _serviceProvider.GetRequiredService<ILogger<ExcelRenderer>>());

            // 4. For each burst value (or single run)
            foreach (var burstValue in burstValues)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var burstParams = new Dictionary<string, string>();
                    if (config.BurstEnabled && !string.IsNullOrEmpty(config.BurstParameterName))
                    {
                        burstParams[config.BurstParameterName] = burstValue;
                    }

                    // Generate file name
                    var baseName = template.Name ?? $"report_{config.TemplateId}";
                    var fileName = config.BurstEnabled
                        ? $"{baseName}_{SanitizeForFileName(burstValue)}"
                        : baseName;

                    // Resolve formulas and render output
                    byte[] outputBytes;
                    string contentType;
                    string extension;

                    switch (config.OutputFormat.ToUpperInvariant())
                    {
                        case "CSV":
                            using (var workbook = await excelRenderer.RenderToWorkbookAsync(
                                template.Content, template.DefaultConnectionId, burstParams, ct))
                            {
                                var csvRenderer = new CsvRenderer();
                                outputBytes = csvRenderer.Render(workbook);
                            }
                            contentType = "text/csv";
                            extension = ".csv";
                            break;

                        case "HTML":
                            using (var workbook = await excelRenderer.RenderToWorkbookAsync(
                                template.Content, template.DefaultConnectionId, burstParams, ct))
                            {
                                outputBytes = RenderWorkbookToHtml(workbook, baseName);
                            }
                            contentType = "text/html";
                            extension = ".html";
                            break;

                        default: // EXCEL
                            outputBytes = await excelRenderer.RenderAsync(
                                template.Content, template.DefaultConnectionId, burstParams, ct);
                            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                            extension = ".xlsx";
                            break;
                    }

                    result.ReportsGenerated++;

                    // 5. Deliver if not preview
                    if (deliver)
                    {
                        var channelConfig = new ChannelConfig
                        {
                            ChannelType = config.ChannelType,
                            Settings = config.ChannelSettings,
                            Recipients = config.Recipients
                        };

                        var channel = ResolveChannel(config.ChannelType);
                        var delivered = await channel.DeliverAsync(
                            $"{fileName}{extension}", outputBytes, contentType, channelConfig, ct);

                        if (delivered)
                            result.ReportsDelivered++;
                        else
                            result.Errors.Add($"Delivery failed for burst value: {burstValue}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing burst value: {BurstValue}", burstValue);
                    result.Errors.Add($"Error for '{burstValue}': {ex.Message}");
                }
            }

            result.Success = result.Errors.Count == 0;
            result.DurationMs = sw.ElapsedMilliseconds;

            // 6. Audit log
            await AuditAsync(config, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Distribution engine failed for template {TemplateId}", config.TemplateId);
            result.Errors.Add($"Fatal error: {ex.Message}");
            result.DurationMs = sw.ElapsedMilliseconds;
            return result;
        }
    }

    // ── Burst Resolution ─────────────────────────────────────────────────────────

    private async Task<List<string>> ResolveBurstValuesAsync(DistributionConfig config, CancellationToken ct)
    {
        if (!config.BurstEnabled)
            return new List<string> { "" }; // Single run with empty burst value

        // If explicit burst values are provided, use them
        if (config.BurstValues != null && config.BurstValues.Count > 0)
            return config.BurstValues;

        // If a burst query is provided, execute it to get the values
        if (!string.IsNullOrWhiteSpace(config.BurstQuery) && config.BurstQueryConnectionId.HasValue)
        {
            var request = new Models.DistributionConfig(); // We need the query service
            var queryRequest = new SpreadsheetServer.Models.QueryRequest
            {
                ConnectionId = config.BurstQueryConnectionId.Value,
                QueryNameOrSql = config.BurstQuery,
                MaxRows = 10000
            };

            var queryResult = await _queryService.ExecuteQueryAsync(queryRequest, 0);
            if (!string.IsNullOrEmpty(queryResult.Error))
                throw new InvalidOperationException($"Burst query failed: {queryResult.Error}");

            // Take the first column value from each row
            return queryResult.Rows
                .Where(r => r.Length > 0 && r[0] != null)
                .Select(r => r[0]!.ToString() ?? "")
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();
        }

        return new List<string> { "" }; // Fallback to single run
    }

    // ── Template Loading ─────────────────────────────────────────────────────────

    private async Task<ExcelTemplate?> LoadTemplateAsync(long templateId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ExcelTemplate>(
            @"SELECT id AS Id, name AS Name, file_content AS Content,
                     default_connection_id AS DefaultConnectionId
              FROM RS_EXCEL_TEMPLATE
              WHERE id = @Id",
            new { Id = templateId });
    }

    // ── Channel Resolution ───────────────────────────────────────────────────────

    private IDistributionChannel ResolveChannel(string channelType)
    {
        return channelType.ToUpperInvariant() switch
        {
            "EMAIL" => new EmailChannel(
                _database,
                _serviceProvider.GetRequiredService<ILogger<EmailChannel>>()),
            "FILESYSTEM" or _ => new FileSystemChannel(
                _serviceProvider.GetRequiredService<ILogger<FileSystemChannel>>())
        };
    }

    // ── HTML Renderer (inline, simple) ───────────────────────────────────────────

    private static byte[] RenderWorkbookToHtml(XLWorkbook workbook, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(@"
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 20px; }
            table { border-collapse: collapse; width: 100%; margin-bottom: 30px; }
            th { background-color: #f8f9fa; border: 1px solid #dee2e6; padding: 8px 12px; text-align: left; }
            td { border: 1px solid #dee2e6; padding: 6px 12px; }
            tr:nth-child(even) { background-color: #f8f9fa; }
            h2 { color: #333; }
            .footer { font-size: 12px; color: #6c757d; margin-top: 20px; }
        ");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        foreach (var ws in workbook.Worksheets)
        {
            var range = ws.RangeUsed();
            if (range == null) continue;

            sb.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(ws.Name)}</h2>");
            sb.AppendLine("<table>");

            var firstRow = range.FirstRow().RowNumber();
            var lastRow = range.LastRow().RowNumber();
            var firstCol = range.FirstColumn().ColumnNumber();
            var lastCol = range.LastColumn().ColumnNumber();

            for (int row = firstRow; row <= lastRow; row++)
            {
                sb.Append("<tr>");
                var tag = row == firstRow ? "th" : "td";
                for (int col = firstCol; col <= lastCol; col++)
                {
                    var cell = ws.Cell(row, col);
                    var val = cell.IsEmpty() ? "" : (cell.Value.ToString() ?? "");
                    sb.Append($"<{tag}>{System.Net.WebUtility.HtmlEncode(val)}</{tag}>");
                }
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");
        }

        sb.AppendLine($"<div class=\"footer\">Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</div>");
        sb.AppendLine("</body></html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Audit ────────────────────────────────────────────────────────────────────

    private async Task AuditAsync(DistributionConfig config, DistributionResult result)
    {
        try
        {
            var details = $"Template={config.TemplateId}, Format={config.OutputFormat}, " +
                          $"Channel={config.ChannelType}, Generated={result.ReportsGenerated}, " +
                          $"Delivered={result.ReportsDelivered}, Errors={result.Errors.Count}, " +
                          $"Duration={result.DurationMs}ms";

            await _auditRepository.LogAsync(0, "DISTRIBUTION", "RS_EXCEL_TEMPLATE",
                config.TemplateId, details, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write distribution audit log");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string SanitizeForFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // ── Internal Models ──────────────────────────────────────────────────────────

    private class ExcelTemplate
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public long DefaultConnectionId { get; set; }
    }
}
