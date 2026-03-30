using System.Diagnostics;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.Services;
using MySqlConnector;

namespace DaisyReport.Api.ReportEngine;

public interface IReportExecutionPipeline
{
    Task<ReportExecutionResult> ExecuteAsync(ReportExecutionRequest request);
}

public class ReportExecutionRequest
{
    public long ReportId { get; set; }
    public long UserId { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();
    public string OutputFormat { get; set; } = "HTML";
    public int? PageSize { get; set; }
    public int? Page { get; set; }
}

public class ReportExecutionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string ContentType { get; set; } = "text/html";
    public byte[]? Data { get; set; }
    public string? DataAsString { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
    public List<ColumnInfo>? Columns { get; set; }
    public int? TotalRows { get; set; }
    public long ExecutionTimeMs { get; set; }
    public string? GeneratedSql { get; set; }
}

public class ReportExecutionPipeline : IReportExecutionPipeline
{
    private readonly IReportRepository _reportRepo;
    private readonly IDatasourceRepository _datasourceRepo;
    private readonly IAclService _aclService;
    private readonly IAuditRepository _auditRepo;
    private readonly IOutputFormatter _outputFormatter;
    private readonly EngineRouter _engineRouter;
    private readonly ILogger<ReportExecutionPipeline> _logger;

    public ReportExecutionPipeline(
        IReportRepository reportRepo,
        IDatasourceRepository datasourceRepo,
        IAclService aclService,
        IAuditRepository auditRepo,
        IOutputFormatter outputFormatter,
        EngineRouter engineRouter,
        ILogger<ReportExecutionPipeline> logger)
    {
        _reportRepo = reportRepo;
        _datasourceRepo = datasourceRepo;
        _aclService = aclService;
        _auditRepo = auditRepo;
        _outputFormatter = outputFormatter;
        _engineRouter = engineRouter;
        _logger = logger;
    }

    public async Task<ReportExecutionResult> ExecuteAsync(ReportExecutionRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var context = new ExecutionContext();

        try
        {
            // Phase 1: INIT - Load report definition, validate it exists
            await Phase1_Initialize(context, request);

            // Phase 2: AUTH - Check user has EXECUTE permission
            await Phase2_Authorize(context, request.UserId);

            // Phase 3: PARAMS - Resolve parameters (defaults, expressions, type coercion)
            await Phase3_ResolveParameters(context, request.Parameters);

            // Phase 4: DATASOURCE - Acquire database connection
            await Phase4_BindDatasource(context);

            // Phase 5: EXECUTE - Run engine-specific report generation
            var result = await Phase5_Execute(context, request);

            // Phase 6: OUTPUT - Format output (HTML/PDF/CSV/Excel/JSON)
            result = await Phase6_FormatOutput(context, result, request.OutputFormat);

            // Phase 7: AUDIT - Log execution to audit trail
            await Phase7_Audit(context, request, stopwatch.ElapsedMilliseconds, true);

            // Phase 8: CLEANUP - Release resources
            Phase8_Cleanup(context);

            result.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report execution failed for report {ReportId}", request.ReportId);
            await Phase7_Audit(context, request, stopwatch.ElapsedMilliseconds, false, ex.Message);
            Phase8_Cleanup(context);
            return new ReportExecutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    // ─── Phase 1: Initialize ────────────────────────────────────────────

    private async Task Phase1_Initialize(ExecutionContext context, ReportExecutionRequest request)
    {
        _logger.LogDebug("Phase 1: Loading report {ReportId}", request.ReportId);

        var report = await _reportRepo.GetByIdAsync(request.ReportId);
        if (report == null)
        {
            throw new KeyNotFoundException($"Report {request.ReportId} not found.");
        }

        context.Report = report;

        // Load parameter definitions
        context.ParameterDefinitions = await _reportRepo.GetParametersAsync(request.ReportId);
    }

    // ─── Phase 2: Authorize ─────────────────────────────────────────────

    private async Task Phase2_Authorize(ExecutionContext context, long userId)
    {
        _logger.LogDebug("Phase 2: Checking EXECUTE permission for user {UserId} on report {ReportId}",
            userId, context.Report!.Id);

        var allowed = await _aclService.CheckPermissionAsync(userId, "report", context.Report.Id, "EXECUTE");
        if (!allowed)
        {
            // Fall back to legacy permission check
            var hasLegacy = await _aclService.HasPermissionAsync(userId, "report.execute");
            if (!hasLegacy)
            {
                throw new UnauthorizedAccessException(
                    $"User {userId} does not have EXECUTE permission on report {context.Report.Id}.");
            }
        }
    }

    // ─── Phase 3: Resolve Parameters ────────────────────────────────────

    private async Task Phase3_ResolveParameters(ExecutionContext context, Dictionary<string, string> supplied)
    {
        _logger.LogDebug("Phase 3: Resolving {Count} parameter definitions", context.ParameterDefinitions?.Count ?? 0);

        if (context.ParameterDefinitions == null || context.ParameterDefinitions.Count == 0)
        {
            context.ResolvedParameters = new Dictionary<string, object?>();
            return;
        }

        var resolver = new ParameterResolver();
        context.ResolvedParameters = await resolver.ResolveAsync(context.ParameterDefinitions, supplied);
    }

    // ─── Phase 4: Bind Datasource ───────────────────────────────────────

    private async Task Phase4_BindDatasource(ExecutionContext context)
    {
        var report = context.Report!;

        if (!report.DatasourceId.HasValue)
        {
            throw new InvalidOperationException($"Report {report.Id} has no datasource configured.");
        }

        _logger.LogDebug("Phase 4: Binding datasource {DatasourceId}", report.DatasourceId.Value);

        var datasource = await _datasourceRepo.GetByIdAsync(report.DatasourceId.Value);
        if (datasource == null)
        {
            throw new KeyNotFoundException($"Datasource {report.DatasourceId.Value} not found.");
        }

        if (datasource.Dtype != "database")
        {
            throw new InvalidOperationException($"Datasource {datasource.Id} is not a database datasource (type: {datasource.Dtype}).");
        }

        context.Datasource = datasource;

        // Build connection string from JDBC URL
        var connString = ConvertJdbcToConnectionString(
            datasource.JdbcUrl ?? string.Empty,
            datasource.Username,
            datasource.PasswordEncrypted);

        if (datasource.QueryTimeout.HasValue)
        {
            connString += $"DefaultCommandTimeout={datasource.QueryTimeout.Value};";
        }

        var connection = new MySqlConnection(connString);
        await connection.OpenAsync();
        context.Connection = connection;
    }

    // ─── Phase 5: Execute ───────────────────────────────────────────────

    private async Task<ReportExecutionResult> Phase5_Execute(ExecutionContext context, ReportExecutionRequest request)
    {
        _logger.LogDebug("Phase 5: Executing report with engine {EngineType}", context.Report!.EngineType);
        return await _engineRouter.RouteAsync(context, request);
    }

    // ─── Phase 6: Format Output ─────────────────────────────────────────

    private async Task<ReportExecutionResult> Phase6_FormatOutput(
        ExecutionContext context, ReportExecutionResult result, string outputFormat)
    {
        _logger.LogDebug("Phase 6: Formatting output as {Format}", outputFormat);
        return await _outputFormatter.FormatAsync(result, outputFormat);
    }

    // ─── Phase 7: Audit ─────────────────────────────────────────────────

    private async Task Phase7_Audit(
        ExecutionContext context, ReportExecutionRequest request,
        long elapsedMs, bool success, string? errorMessage = null)
    {
        try
        {
            var details = success
                ? $"Report executed in {elapsedMs}ms, format={request.OutputFormat}, rows={context.Report?.Name ?? "?"}"
                : $"Report execution failed after {elapsedMs}ms: {errorMessage}";

            await _auditRepo.LogAsync(
                request.UserId,
                success ? "REPORT_EXECUTE" : "REPORT_EXECUTE_FAIL",
                "report",
                request.ReportId,
                details,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write audit log for report execution");
        }
    }

    // ─── Phase 8: Cleanup ───────────────────────────────────────────────

    private void Phase8_Cleanup(ExecutionContext context)
    {
        try
        {
            if (context.Connection != null)
            {
                context.Connection.Dispose();
                context.Connection = null;
                _logger.LogDebug("Phase 8: Database connection released");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static string ConvertJdbcToConnectionString(string jdbcUrl, string? username, string? password)
    {
        var url = jdbcUrl.Replace("jdbc:mysql://", "");
        var parts = url.Split('/');
        var hostPort = parts[0].Split(':');
        var host = hostPort[0];
        var port = hostPort.Length > 1 ? hostPort[1] : "3306";
        var database = parts.Length > 1 ? parts[1].Split('?')[0] : "";

        return $"Server={host};Port={port};Database={database};User={username};Password={password};";
    }
}
