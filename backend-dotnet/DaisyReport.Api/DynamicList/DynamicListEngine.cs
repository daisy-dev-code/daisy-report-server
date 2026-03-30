using System.Data;
using System.Text.Json;
using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Repositories;
using MySqlConnector;
using Serilog;

namespace DaisyReport.Api.DynamicList;

// ── Contracts ──────────────────────────────────────────────────────────────────

public interface IDynamicListEngine
{
    Task<DynamicListResult> ExecuteAsync(long reportId, DynamicListRequest request);
    Task<DynamicListResult> PreviewAsync(long reportId, int maxRows = 50);
    Task<byte[]> ExportAsync(long reportId, DynamicListRequest request, string format);
}

// ── Request / Response Models ──────────────────────────────────────────────────

public class DynamicListRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
    public List<ColumnSelection>? Columns { get; set; }
    public List<FilterDefinition>? Filters { get; set; }
    public List<SortDefinition>? Sorts { get; set; }
    public AggregationConfig? Aggregation { get; set; }
    public PivotConfig? Pivot { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool Distinct { get; set; }
    public string? Format { get; set; } // HTML, JSON, CSV, EXCEL, PDF
}

public class ColumnSelection
{
    public string Name { get; set; } = "";
    public string? Alias { get; set; }
    public bool Hidden { get; set; }
    public string? Format { get; set; }
    public int? Width { get; set; }
    public string? ComputedExpression { get; set; }
}

public class SortDefinition
{
    public string ColumnName { get; set; } = "";
    public string Direction { get; set; } = "ASC"; // ASC or DESC
}

public class DynamicListResult
{
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public string? GeneratedSql { get; set; }
}

public class ColumnInfo
{
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool Hidden { get; set; }
    public string? Format { get; set; }
    public int? Width { get; set; }
}

// ── Engine Implementation ──────────────────────────────────────────────────────

public class DynamicListEngine : IDynamicListEngine
{
    private readonly IDatabase _database;
    private readonly IReportRepository _reportRepository;
    private readonly IDatasourceRepository _datasourceRepository;
    private readonly SqlGenerator _sqlGenerator;
    private readonly FilterCompiler _filterCompiler;
    private readonly AggregationEngine _aggregationEngine;
    private readonly ComputedColumnEngine _computedColumnEngine;
    private readonly PivotTransformer _pivotTransformer;
    private readonly IExportService _exportService;

    public DynamicListEngine(
        IDatabase database,
        IReportRepository reportRepository,
        IDatasourceRepository datasourceRepository,
        IExportService exportService)
    {
        _database = database;
        _reportRepository = reportRepository;
        _datasourceRepository = datasourceRepository;
        _exportService = exportService;

        _filterCompiler = new FilterCompiler();
        _sqlGenerator = new SqlGenerator(_filterCompiler);
        _aggregationEngine = new AggregationEngine();
        _computedColumnEngine = new ComputedColumnEngine();
        _pivotTransformer = new PivotTransformer();
    }

    public async Task<DynamicListResult> ExecuteAsync(long reportId, DynamicListRequest request)
    {
        var (report, queryText, connection) = await ResolveReportContextAsync(reportId, request.Parameters);

        try
        {
            // Parse report config for column metadata
            var reportConfig = ParseReportConfig(report.Config);

            // Resolve columns — use request columns if provided, else fall back to config
            var columns = ResolveColumns(request.Columns, reportConfig);

            // Compile computed columns
            var columnAliases = columns
                .Where(c => !string.IsNullOrEmpty(c.Alias))
                .ToDictionary(c => c.Name, c => c.Alias!);

            foreach (var col in columns.Where(c => !string.IsNullOrEmpty(c.ComputedExpression)))
            {
                col.ComputedExpression = _computedColumnEngine.CompileColumn(
                    col.ComputedExpression!, columnAliases);
            }

            // Build aggregation info
            AggregationResult? aggResult = null;
            if (request.Aggregation != null)
            {
                aggResult = _aggregationEngine.Apply(
                    columns.Select(c => new ColumnInfo
                    {
                        Name = c.Name,
                        Alias = c.Alias ?? c.Name,
                        Hidden = c.Hidden
                    }).ToList(),
                    request.Aggregation);
            }

            // Build SQL generation context
            var context = new SqlGenerationContext
            {
                QueryText = queryText,
                Columns = columns,
                Filters = request.Filters,
                Sorts = request.Sorts,
                Aggregation = request.Aggregation,
                AggregationResult = aggResult,
                Page = request.Page,
                PageSize = request.PageSize,
                Distinct = request.Distinct
            };

            // Generate count SQL first
            var (countSql, countParams) = _sqlGenerator.GenerateCount(context);
            var totalRows = await connection.ExecuteScalarAsync<int>(countSql, BuildDapperParams(countParams));

            // Generate data SQL
            var (dataSql, dataParams) = _sqlGenerator.Generate(context);

            // Execute query
            var rows = (await connection.QueryAsync(dataSql, BuildDapperParams(dataParams)))
                .Select(row => (IDictionary<string, object?>)row)
                .Select(row => row.ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();

            // Detect column types from result
            var columnInfos = DetectColumnInfo(columns, rows);

            var result = new DynamicListResult
            {
                Columns = columnInfos,
                Rows = rows,
                TotalRows = totalRows,
                Page = request.Page,
                PageSize = request.PageSize,
                GeneratedSql = dataSql
            };

            // Apply pivot transformation if configured
            if (request.Pivot != null)
            {
                result = _pivotTransformer.Transform(result, request.Pivot);
            }

            return result;
        }
        finally
        {
            connection.Dispose();
        }
    }

    public async Task<DynamicListResult> PreviewAsync(long reportId, int maxRows = 50)
    {
        var request = new DynamicListRequest
        {
            Page = 1,
            PageSize = Math.Min(maxRows, 200)
        };
        return await ExecuteAsync(reportId, request);
    }

    public async Task<byte[]> ExportAsync(long reportId, DynamicListRequest request, string format)
    {
        // For exports, remove pagination to get all rows
        request.Page = 1;
        request.PageSize = 100_000; // safety cap

        var result = await ExecuteAsync(reportId, request);

        return format.ToUpperInvariant() switch
        {
            "CSV" => _exportService.ExportCsv(result),
            "JSON" => _exportService.ExportJson(result),
            "HTML" => _exportService.ExportHtml(result),
            _ => throw new ArgumentException($"Unsupported export format: {format}")
        };
    }

    // ── Private Helpers ────────────────────────────────────────────────────────

    private async Task<(Models.Report Report, string QueryText, IDbConnection Connection)>
        ResolveReportContextAsync(long reportId, Dictionary<string, string>? parameters)
    {
        var report = await _reportRepository.GetByIdAsync(reportId)
            ?? throw new InvalidOperationException($"Report {reportId} not found.");

        if (string.IsNullOrWhiteSpace(report.QueryText))
            throw new InvalidOperationException($"Report {reportId} has no query text defined.");

        // Resolve datasource connection
        IDbConnection connection;
        if (report.DatasourceId.HasValue)
        {
            var datasource = await _datasourceRepository.GetByIdAsync(report.DatasourceId.Value)
                ?? throw new InvalidOperationException($"Datasource {report.DatasourceId} not found.");

            connection = await CreateDatasourceConnectionAsync(datasource);
        }
        else
        {
            connection = await _database.GetConnectionAsync();
        }

        // Substitute query parameters
        var queryText = SubstituteParameters(report.QueryText, parameters);

        return (report, queryText, connection);
    }

    private static async Task<IDbConnection> CreateDatasourceConnectionAsync(Models.Datasource datasource)
    {
        if (datasource.Dtype != "database")
            throw new InvalidOperationException("Only database datasources are supported for Dynamic Lists.");

        var jdbcUrl = datasource.JdbcUrl ?? "";
        var connString = ConvertJdbcToConnectionString(jdbcUrl, datasource.Username, datasource.PasswordEncrypted);

        var conn = new MySqlConnection(connString);
        await conn.OpenAsync();
        return conn;
    }

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

    private static string SubstituteParameters(string queryText, Dictionary<string, string>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return queryText;

        var result = queryText;
        foreach (var (key, value) in parameters)
        {
            // Replace ${param} and :param style placeholders
            result = result.Replace($"${{{key}}}", value);
            result = result.Replace($":{key}", value);
        }
        return result;
    }

    private static ReportConfig ParseReportConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return new ReportConfig();

        try
        {
            return JsonSerializer.Deserialize<ReportConfig>(configJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ReportConfig();
        }
        catch
        {
            return new ReportConfig();
        }
    }

    private static List<ColumnSelection> ResolveColumns(
        List<ColumnSelection>? requestColumns, ReportConfig reportConfig)
    {
        if (requestColumns != null && requestColumns.Count > 0)
            return requestColumns;

        if (reportConfig.Columns != null && reportConfig.Columns.Count > 0)
            return reportConfig.Columns;

        // No columns specified — engine will use SELECT * behavior
        return new List<ColumnSelection>();
    }

    private static List<ColumnInfo> DetectColumnInfo(
        List<ColumnSelection> requestedColumns, List<Dictionary<string, object?>> rows)
    {
        var columnInfos = new List<ColumnInfo>();

        if (rows.Count == 0 && requestedColumns.Count == 0)
            return columnInfos;

        // Use first row keys to detect columns if no explicit columns defined
        var columnNames = rows.Count > 0
            ? rows[0].Keys.ToList()
            : requestedColumns.Select(c => c.Alias ?? c.Name).ToList();

        var requestedLookup = requestedColumns.ToDictionary(
            c => c.Alias ?? c.Name,
            c => c,
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in columnNames)
        {
            var info = new ColumnInfo
            {
                Name = name,
                Alias = name,
                DataType = DetectDataType(name, rows)
            };

            if (requestedLookup.TryGetValue(name, out var sel))
            {
                info.Hidden = sel.Hidden;
                info.Format = sel.Format;
                info.Width = sel.Width;
            }

            columnInfos.Add(info);
        }

        return columnInfos;
    }

    private static string DetectDataType(string columnName, List<Dictionary<string, object?>> rows)
    {
        var sampleValue = rows
            .Select(r => r.TryGetValue(columnName, out var v) ? v : null)
            .FirstOrDefault(v => v != null);

        return sampleValue switch
        {
            int or long or short or byte => "INTEGER",
            decimal or double or float => "DECIMAL",
            DateTime => "DATETIME",
            bool => "BOOLEAN",
            _ => "STRING"
        };
    }

    private static DynamicParameters BuildDapperParams(List<object> paramValues)
    {
        var dp = new DynamicParameters();
        for (int i = 0; i < paramValues.Count; i++)
        {
            dp.Add($"p{i}", paramValues[i]);
        }
        return dp;
    }
}

// ── Supporting Config Model ────────────────────────────────────────────────────

public class ReportConfig
{
    public List<ColumnSelection>? Columns { get; set; }
    public List<FilterDefinition>? Prefilters { get; set; }
    public List<SortDefinition>? DefaultSorts { get; set; }
}
