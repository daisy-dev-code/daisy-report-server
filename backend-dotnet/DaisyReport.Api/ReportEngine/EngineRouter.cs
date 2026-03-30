using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using DaisyReport.Api.DynamicList;

namespace DaisyReport.Api.ReportEngine;

public class EngineRouter
{
    private readonly ILogger<EngineRouter> _logger;
    private readonly IDynamicListEngine _dynamicListEngine;

    public EngineRouter(ILogger<EngineRouter> logger, IDynamicListEngine dynamicListEngine)
    {
        _logger = logger;
        _dynamicListEngine = dynamicListEngine;
    }

    public async Task<ReportExecutionResult> RouteAsync(ExecutionContext context, ReportExecutionRequest request)
    {
        return (context.Report!.EngineType?.ToUpperInvariant() ?? "DYNAMIC_LIST") switch
        {
            "DYNAMIC_LIST" => await ExecuteViaDynamicListEngine(context, request),
            "SQL" => await ExecuteBasicSql(context, request),
            "SCRIPT" => throw new NotImplementedException("Script engine not yet available."),
            "BIRT" => throw new NotImplementedException("BIRT engine requires Java microservice."),
            "JASPER" => throw new NotImplementedException("Jasper engine requires Java microservice."),
            _ => throw new ArgumentException($"Unknown engine type: {context.Report.EngineType}")
        };
    }

    private async Task<ReportExecutionResult> ExecuteViaDynamicListEngine(
        ExecutionContext context, ReportExecutionRequest request)
    {
        var report = context.Report!;
        _logger.LogDebug("Routing report {ReportId} to DynamicListEngine", report.Id);

        var dlRequest = new DynamicListRequest
        {
            Parameters = request.Parameters.ToDictionary(kv => kv.Key, kv => kv.Value),
            Page = request.Page ?? 1,
            PageSize = request.PageSize ?? 50,
            Format = request.OutputFormat
        };

        var result = await _dynamicListEngine.ExecuteAsync(report.Id, dlRequest);

        return new ReportExecutionResult
        {
            Success = true,
            Rows = result.Rows,
            Columns = result.Columns.Select(c => new ColumnInfo
            {
                Name = c.Name,
                DataType = c.DataType,
                Label = c.Alias,
                OrdinalPosition = 0
            }).ToList(),
            TotalRows = result.TotalRows,
            GeneratedSql = result.GeneratedSql
        };
    }

    private async Task<ReportExecutionResult> ExecuteBasicSql(ExecutionContext context, ReportExecutionRequest request)
    {
        var report = context.Report!;
        var connection = context.Connection!;

        if (string.IsNullOrWhiteSpace(report.QueryText))
        {
            throw new InvalidOperationException("Report has no query text defined.");
        }

        // Substitute parameters into SQL
        var sql = SubstituteParameters(report.QueryText, context.ResolvedParameters);

        _logger.LogDebug("Executing dynamic list SQL: {Sql}", sql);

        // Build the Dapper parameter object
        var dynParams = new DynamicParameters();
        foreach (var (key, value) in context.ResolvedParameters)
        {
            dynParams.Add(key, value);
        }

        // Execute count query for pagination
        int? totalRows = null;
        if (request.PageSize.HasValue)
        {
            var countSql = $"SELECT COUNT(*) FROM ({sql}) AS __count_query";
            try
            {
                totalRows = await connection.ExecuteScalarAsync<int>(countSql, dynParams);
            }
            catch
            {
                // If count query fails (e.g., complex SQL), skip total count
                _logger.LogWarning("Count query failed for report {ReportId}, skipping total count", report.Id);
            }
        }

        // Apply pagination
        var executeSql = sql;
        if (request.PageSize.HasValue && request.Page.HasValue)
        {
            var offset = (request.Page.Value - 1) * request.PageSize.Value;
            executeSql = $"{sql} LIMIT {request.PageSize.Value} OFFSET {offset}";
        }

        // Execute the query
        var reader = await connection.ExecuteReaderAsync(executeSql, dynParams);
        var columns = new List<ColumnInfo>();
        var rows = new List<Dictionary<string, object?>>();

        // Extract column metadata from the reader's field info
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ColumnInfo
            {
                Name = reader.GetName(i),
                DataType = reader.GetDataTypeName(i) ?? "string",
                Label = reader.GetName(i),
                OrdinalPosition = i
            });
        }

        // Read rows
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var colName = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[colName] = value;
            }
            rows.Add(row);
        }

        await reader.DisposeAsync();

        return new ReportExecutionResult
        {
            Success = true,
            Rows = rows,
            Columns = columns,
            TotalRows = totalRows ?? rows.Count,
            GeneratedSql = executeSql
        };
    }

    /// <summary>
    /// Replace ${param_name} placeholders in SQL with @param_name for parameterized queries.
    /// Also supports :param_name syntax used by some report definitions.
    /// </summary>
    private static string SubstituteParameters(string sql, Dictionary<string, object?> parameters)
    {
        var result = sql;

        // Replace ${param_name} with @param_name (Dapper parameter syntax)
        result = Regex.Replace(result, @"\$\{(\w+)\}", m =>
        {
            var paramName = m.Groups[1].Value;
            return parameters.ContainsKey(paramName) ? $"@{paramName}" : m.Value;
        });

        // Replace :param_name with @param_name (JDBC-style parameter syntax)
        result = Regex.Replace(result, @":(\w+)", m =>
        {
            var paramName = m.Groups[1].Value;
            return parameters.ContainsKey(paramName) ? $"@{paramName}" : m.Value;
        });

        return result;
    }
}
