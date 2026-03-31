using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Repositories;
using DaisyReport.Api.SpreadsheetServer.Models;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Serilog;

namespace DaisyReport.Api.SpreadsheetServer.Services;

public interface ISpreadsheetQueryService
{
    Task<QueryResult> ExecuteQueryAsync(QueryRequest request, long userId);
    Task<ScalarResult> ExecuteAggregateAsync(AggregateRequest request, long userId);
    Task<ScalarResult> ExecuteLookupAsync(LookupRequest request, long userId);
    Task<ScalarResult> GetGlBalanceAsync(GlBalanceRequest request, long userId);
    Task<QueryResult> GetGlDetailAsync(GlDetailRequest request, long userId);
    Task<ScalarResult> GetGlRangeAsync(GlRangeRequest request, long userId);
    Task<QueryResult> ExecuteDrilldownAsync(DrilldownRequest request, long userId);
}

public class SpreadsheetQueryService : ISpreadsheetQueryService
{
    private readonly IDatabase _database;
    private readonly IDatasourceRepository _datasourceRepository;
    private readonly IRedisCache _redisCache;
    private readonly IAuditRepository _auditRepository;
    private readonly ILogger<SpreadsheetQueryService> _logger;

    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);
    private static readonly string[] AllowedAggregateFunctions = { "SUM", "AVG", "COUNT", "MIN", "MAX" };
    private static readonly string[] AllowedBalanceTypes = { "PTD", "YTD", "QTD", "BAL", "BEG" };

    public SpreadsheetQueryService(
        IDatabase database,
        IDatasourceRepository datasourceRepository,
        IRedisCache redisCache,
        IAuditRepository auditRepository,
        ILogger<SpreadsheetQueryService> logger)
    {
        _database = database;
        _datasourceRepository = datasourceRepository;
        _redisCache = redisCache;
        _auditRepository = auditRepository;
        _logger = logger;
    }

    // ── Query Execution ───────────────────────────────────────────────────────

    public async Task<QueryResult> ExecuteQueryAsync(QueryRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = BuildCacheKey("query", request.ConnectionId, request.QueryNameOrSql, request.Parameters);

        // Check cache
        var cached = await _redisCache.GetAsync<QueryResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var sql = await ResolveQuerySqlAsync(request.ConnectionId, request.QueryNameOrSql);
            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);

            var dp = BuildDapperParams(request.Parameters);
            var cmd = new CommandDefinition(sql, dp, commandTimeout: request.TimeoutSeconds);
            var reader = await conn.ExecuteReaderAsync(cmd);

            var result = ReadQueryResult(reader, request.MaxRows);
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;

            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_QUERY", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpreadsheetQuery failed for connection {ConnectionId}", request.ConnectionId);
            return new QueryResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<ScalarResult> ExecuteAggregateAsync(AggregateRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var func = request.AggregateFunction.ToUpperInvariant();
        if (!AllowedAggregateFunctions.Contains(func))
            return new ScalarResult { Error = $"Invalid aggregate function: {func}" };

        var cacheKey = BuildCacheKey("agg", request.ConnectionId, request.QueryNameOrSql,
            request.Parameters, func, request.AggregateColumn);

        var cached = await _redisCache.GetAsync<ScalarResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var innerSql = await ResolveQuerySqlAsync(request.ConnectionId, request.QueryNameOrSql);
            var safeCol = SanitizeIdentifier(request.AggregateColumn);
            var sql = $"SELECT {func}(`{safeCol}`) FROM ({innerSql}) AS _agg";

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            var dp = BuildDapperParams(request.Parameters);
            var value = await conn.ExecuteScalarAsync(sql, dp);

            var result = new ScalarResult { Value = value, ExecutionTimeMs = sw.ElapsedMilliseconds };
            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_AGGREGATE", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpreadsheetAggregate failed for connection {ConnectionId}", request.ConnectionId);
            return new ScalarResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<ScalarResult> ExecuteLookupAsync(LookupRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = BuildCacheKey("lookup", request.ConnectionId, request.QueryNameOrSql,
            request.Parameters, request.ReturnColumn, request.LookupColumn,
            request.LookupValue?.ToString() ?? "");

        var cached = await _redisCache.GetAsync<ScalarResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var innerSql = await ResolveQuerySqlAsync(request.ConnectionId, request.QueryNameOrSql);
            var safeReturn = SanitizeIdentifier(request.ReturnColumn);
            var safeLookup = SanitizeIdentifier(request.LookupColumn);
            var sql = $"SELECT `{safeReturn}` FROM ({innerSql}) AS _lkp WHERE `{safeLookup}` = @lookupVal LIMIT 1";

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            var dp = BuildDapperParams(request.Parameters);
            dp.Add("lookupVal", request.LookupValue);

            var value = await conn.ExecuteScalarAsync(sql, dp);
            var result = new ScalarResult { Value = value, ExecutionTimeMs = sw.ElapsedMilliseconds };

            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_LOOKUP", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpreadsheetLookup failed for connection {ConnectionId}", request.ConnectionId);
            return new ScalarResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    // ── GL Formulas ───────────────────────────────────────────────────────────

    public async Task<ScalarResult> GetGlBalanceAsync(GlBalanceRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var balType = request.BalanceType.ToUpperInvariant();
        if (!AllowedBalanceTypes.Contains(balType))
            return new ScalarResult { Error = $"Invalid balance type: {balType}" };

        var cacheKey = BuildCacheKey("glbal", request.ConnectionId, request.Account,
            null, balType, request.Period.ToString(), request.Year.ToString());

        var cached = await _redisCache.GetAsync<ScalarResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var config = await GetErpConfigAsync(request.ConnectionId);
            if (config == null)
                return new ScalarResult { Error = "GL connector not configured for this datasource" };

            if (string.IsNullOrWhiteSpace(config.GlBalanceQuery))
                return new ScalarResult { Error = "GL balance query template not configured" };

            var sql = config.GlBalanceQuery
                .Replace("{Account}", request.Account)
                .Replace("{Period}", request.Period.ToString())
                .Replace("{Year}", request.Year.ToString())
                .Replace("{BalanceType}", balType);

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            var value = await conn.ExecuteScalarAsync(sql);

            var result = new ScalarResult { Value = value, ExecutionTimeMs = sw.ElapsedMilliseconds };
            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_GL_BALANCE", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Balance failed for connection {ConnectionId}", request.ConnectionId);
            return new ScalarResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<QueryResult> GetGlDetailAsync(GlDetailRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var cacheKey = BuildCacheKey("gldet", request.ConnectionId, request.Account,
            null, request.Period.ToString(), request.Year.ToString());

        var cached = await _redisCache.GetAsync<QueryResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var config = await GetErpConfigAsync(request.ConnectionId);
            if (config == null)
                return new QueryResult { Error = "GL connector not configured for this datasource" };

            if (string.IsNullOrWhiteSpace(config.GlDetailQuery))
                return new QueryResult { Error = "GL detail query template not configured" };

            var sql = config.GlDetailQuery
                .Replace("{Account}", request.Account)
                .Replace("{Period}", request.Period.ToString())
                .Replace("{Year}", request.Year.ToString());

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            using var reader = await conn.ExecuteReaderAsync(sql);

            var result = ReadQueryResult(reader, request.MaxRows);
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;

            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_GL_DETAIL", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Detail failed for connection {ConnectionId}", request.ConnectionId);
            return new QueryResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    public async Task<ScalarResult> GetGlRangeAsync(GlRangeRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();
        var balType = request.BalanceType.ToUpperInvariant();
        if (!AllowedBalanceTypes.Contains(balType))
            return new ScalarResult { Error = $"Invalid balance type: {balType}" };

        var cacheKey = BuildCacheKey("glrng", request.ConnectionId, request.AccountFrom,
            null, request.AccountTo, balType, request.Period.ToString(), request.Year.ToString());

        var cached = await _redisCache.GetAsync<ScalarResult>(cacheKey);
        if (cached != null)
        {
            cached.Cached = true;
            cached.ExecutionTimeMs = sw.ElapsedMilliseconds;
            return cached;
        }

        try
        {
            var config = await GetErpConfigAsync(request.ConnectionId);
            if (config == null)
                return new ScalarResult { Error = "GL connector not configured for this datasource" };

            if (string.IsNullOrWhiteSpace(config.GlRangeQuery))
                return new ScalarResult { Error = "GL range query template not configured" };

            var sql = config.GlRangeQuery
                .Replace("{AccountFrom}", request.AccountFrom)
                .Replace("{AccountTo}", request.AccountTo)
                .Replace("{Period}", request.Period.ToString())
                .Replace("{Year}", request.Year.ToString())
                .Replace("{BalanceType}", balType);

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            var value = await conn.ExecuteScalarAsync(sql);

            var result = new ScalarResult { Value = value, ExecutionTimeMs = sw.ElapsedMilliseconds };
            await _redisCache.SetAsync(cacheKey, result, CacheExpiry);
            await AuditAsync(userId, "SS_GL_RANGE", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GL Range failed for connection {ConnectionId}", request.ConnectionId);
            return new ScalarResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    // ── Drilldown ─────────────────────────────────────────────────────────────

    public async Task<QueryResult> ExecuteDrilldownAsync(DrilldownRequest request, long userId)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var innerSql = await ResolveQuerySqlAsync(request.ConnectionId, request.SourceQuery);
            var dp = BuildDapperParams(request.Parameters);

            string sql;
            if (!string.IsNullOrWhiteSpace(request.DrilldownColumn) && request.DrilldownValue != null)
            {
                var safeCol = SanitizeIdentifier(request.DrilldownColumn);
                sql = $"SELECT * FROM ({innerSql}) AS _drill WHERE `{safeCol}` = @drillVal LIMIT @maxRows";
                dp.Add("drillVal", request.DrilldownValue);
                dp.Add("maxRows", request.MaxRows);
            }
            else
            {
                sql = $"SELECT * FROM ({innerSql}) AS _drill LIMIT @maxRows";
                dp.Add("maxRows", request.MaxRows);
            }

            using var conn = await OpenDatasourceConnectionAsync(request.ConnectionId);
            using var reader = await conn.ExecuteReaderAsync(sql, dp);

            var result = ReadQueryResult(reader, request.MaxRows);
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;

            await AuditAsync(userId, "SS_DRILLDOWN", request.ConnectionId, sql);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SpreadsheetDrilldown failed for connection {ConnectionId}", request.ConnectionId);
            return new QueryResult { Error = ex.Message, ExecutionTimeMs = sw.ElapsedMilliseconds };
        }
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the SQL text: if it looks like a saved query name (no spaces, no SELECT),
    /// look it up in RS_SAVED_QUERY; otherwise treat as raw SQL.
    /// </summary>
    private async Task<string> ResolveQuerySqlAsync(long connectionId, string queryNameOrSql)
    {
        var trimmed = queryNameOrSql.Trim();

        // If it looks like SQL (contains SELECT, WITH, etc.), use as-is
        if (LooksLikeSql(trimmed))
            return trimmed;

        // Try to resolve as a saved query name
        using var conn = await _database.GetConnectionAsync();
        var savedSql = await conn.ExecuteScalarAsync<string?>(
            @"SELECT sql_text FROM RS_SAVED_QUERY
              WHERE (name = @Name OR id = @IdAttempt) AND datasource_id = @DsId
              LIMIT 1",
            new
            {
                Name = trimmed,
                IdAttempt = long.TryParse(trimmed, out var id) ? id : 0,
                DsId = connectionId
            });

        if (!string.IsNullOrWhiteSpace(savedSql))
            return savedSql;

        // Fall back to treating it as SQL
        return trimmed;
    }

    private static bool LooksLikeSql(string text)
    {
        var upper = text.TrimStart().ToUpperInvariant();
        return upper.StartsWith("SELECT") || upper.StartsWith("WITH") ||
               upper.StartsWith("CALL") || upper.StartsWith("EXEC") ||
               upper.StartsWith("SHOW") || upper.StartsWith("DESCRIBE");
    }

    /// <summary>
    /// Opens a connection to an external datasource by reading its config from RS_DATASOURCE.
    /// </summary>
    private async Task<IDbConnection> OpenDatasourceConnectionAsync(long connectionId)
    {
        var datasource = await _datasourceRepository.GetByIdAsync(connectionId)
            ?? throw new InvalidOperationException($"Datasource {connectionId} not found.");

        if (string.IsNullOrWhiteSpace(datasource.JdbcUrl) && string.IsNullOrWhiteSpace(datasource.DriverClass))
            throw new InvalidOperationException($"Datasource {connectionId} has no connection info configured.");

        var jdbcUrl = datasource.JdbcUrl ?? "";
        var driverClass = datasource.DriverClass ?? "";
        var connString = ConvertJdbcToConnectionString(jdbcUrl, datasource.Username, datasource.PasswordEncrypted);
        var provider = DetectProvider(jdbcUrl, driverClass);

        IDbConnection conn = provider switch
        {
            DbProvider.SqlServer => new SqlConnection(connString),
            DbProvider.PostgreSql => new NpgsqlConnection(connString),
            _ => new MySqlConnection(connString)
        };

        if (conn is System.Data.Common.DbConnection dbConn)
            await dbConn.OpenAsync();
        else
            conn.Open();

        return conn;
    }

    private static string ConvertJdbcToConnectionString(string jdbcUrl, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(jdbcUrl))
            return "";

        // If no jdbc: prefix, assume it's already a native connection string
        if (!jdbcUrl.StartsWith("jdbc:", StringComparison.OrdinalIgnoreCase))
            return jdbcUrl;

        if (jdbcUrl.StartsWith("jdbc:mysql://", StringComparison.OrdinalIgnoreCase))
        {
            var url = jdbcUrl["jdbc:mysql://".Length..];
            var (host, port, database) = ParseHostPortDb(url, "3306");
            return $"Server={host};Port={port};Database={database};User={username};Password={password};";
        }

        if (jdbcUrl.StartsWith("jdbc:sqlserver://", StringComparison.OrdinalIgnoreCase))
        {
            var url = jdbcUrl["jdbc:sqlserver://".Length..];
            var semicolonIdx = url.IndexOf(';');
            var hostPort = semicolonIdx >= 0 ? url[..semicolonIdx] : url;
            var parts = hostPort.Split(':');
            var host = parts[0];
            var port = parts.Length > 1 ? parts[1] : "1433";

            // Parse additional JDBC properties (e.g., ;databaseName=mydb)
            var database = "";
            if (semicolonIdx >= 0)
            {
                var props = url[(semicolonIdx + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var prop in props)
                {
                    var kv = prop.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("databaseName", StringComparison.OrdinalIgnoreCase))
                        database = kv[1];
                }
            }

            return $"Server={host},{port};Database={database};User Id={username};Password={password};TrustServerCertificate=True;";
        }

        if (jdbcUrl.StartsWith("jdbc:postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var url = jdbcUrl["jdbc:postgresql://".Length..];
            var (host, port, database) = ParseHostPortDb(url, "5432");
            return $"Host={host};Port={port};Database={database};Username={username};Password={password};";
        }

        // Unknown JDBC prefix — strip jdbc: and try as-is
        return jdbcUrl.StartsWith("jdbc:") ? jdbcUrl[5..] : jdbcUrl;
    }

    private static (string Host, string Port, string Database) ParseHostPortDb(string url, string defaultPort)
    {
        var pathIdx = url.IndexOf('/');
        string hostPort, rest;
        if (pathIdx >= 0)
        {
            hostPort = url[..pathIdx];
            rest = url[(pathIdx + 1)..];
        }
        else
        {
            hostPort = url;
            rest = "";
        }

        var hp = hostPort.Split(':');
        var host = hp[0];
        var port = hp.Length > 1 ? hp[1] : defaultPort;
        var database = rest.Split('?')[0];
        return (host, port, database);
    }

    private enum DbProvider { MySql, SqlServer, PostgreSql }

    private static DbProvider DetectProvider(string jdbcUrl, string driverClass)
    {
        var combined = (jdbcUrl + " " + driverClass).ToLowerInvariant();

        if (combined.Contains("sqlserver") || combined.Contains("mssql") ||
            combined.Contains("microsoft.data.sqlclient"))
            return DbProvider.SqlServer;

        if (combined.Contains("postgresql") || combined.Contains("npgsql") ||
            combined.Contains("postgres"))
            return DbProvider.PostgreSql;

        return DbProvider.MySql;
    }

    private async Task<ErpConnectorConfig?> GetErpConfigAsync(long datasourceId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ErpConnectorConfig>(
            @"SELECT id, datasource_id, erp_type, gl_balance_query, gl_detail_query,
                     gl_range_query, account_table, account_column, period_table,
                     fiscal_year_start_month, segment_format, config
              FROM RS_ERP_CONNECTOR_CONFIG
              WHERE datasource_id = @DatasourceId",
            new { DatasourceId = datasourceId });
    }

    /// <summary>
    /// Reads an IDataReader into a QueryResult with column metadata and row arrays.
    /// </summary>
    private static QueryResult ReadQueryResult(IDataReader reader, int maxRows)
    {
        var columns = new List<ColumnInfo>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new ColumnInfo
            {
                Name = reader.GetName(i),
                DataType = MapClrTypeToLabel(reader.GetFieldType(i))
            });
        }

        var rows = new List<object?[]>();
        int count = 0;
        while (reader.Read() && count < maxRows)
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
            count++;
        }

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            TotalRows = rows.Count
        };
    }

    private static string MapClrTypeToLabel(Type type)
    {
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return "INTEGER";
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return "DECIMAL";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return "DATETIME";
        if (type == typeof(bool))
            return "BOOLEAN";
        if (type == typeof(byte[]))
            return "BINARY";
        return "STRING";
    }

    private static DynamicParameters BuildDapperParams(Dictionary<string, object?> parameters)
    {
        var dp = new DynamicParameters();
        foreach (var (key, value) in parameters)
        {
            // Convert JsonElement to a primitive CLR type for Dapper
            dp.Add(key, UnwrapJsonElement(value));
        }
        return dp;
    }

    private static object? UnwrapJsonElement(object? value)
    {
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => je.GetRawText()
            };
        }
        return value;
    }

    private static string SanitizeIdentifier(string identifier)
    {
        // Strip anything that isn't alphanumeric or underscore to prevent injection
        return Regex.Replace(identifier, @"[^\w]", "", RegexOptions.None);
    }

    private static string BuildCacheKey(string prefix, long connectionId, string query,
        Dictionary<string, object?>? parameters, params string[] extras)
    {
        var sb = new StringBuilder($"ss:{prefix}:{connectionId}:");
        sb.Append(query);
        if (parameters != null)
        {
            foreach (var (key, value) in parameters.OrderBy(p => p.Key))
                sb.Append($"|{key}={value}");
        }
        foreach (var extra in extras)
            sb.Append($"|{extra}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return $"ss:{prefix}:{Convert.ToHexStringLower(hash)[..16]}";
    }

    private async Task AuditAsync(long userId, string action, long connectionId, string sql)
    {
        try
        {
            await _auditRepository.LogAsync(userId, action, "SPREADSHEET", connectionId,
                sql.Length > 500 ? sql[..500] : sql, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write SS audit log");
        }
    }
}
