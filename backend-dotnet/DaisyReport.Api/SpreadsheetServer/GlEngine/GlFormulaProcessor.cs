using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using DaisyReport.Api.DataProviders;
using DaisyReport.Api.DataProviders.Abstractions;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.SpreadsheetServer.GlEngine.Models;
using DaisyReport.Api.SpreadsheetServer.Models;

namespace DaisyReport.Api.SpreadsheetServer.GlEngine;

public interface IGlFormulaProcessor
{
    Task<decimal?> GetAccountBalanceAsync(long datasourceId, string account, int period, int year, string balanceType);
    Task<List<Dictionary<string, object?>>> GetAccountDetailAsync(long datasourceId, string account, int period, int year, int maxRows = 1000);
    Task<decimal?> GetAccountRangeAsync(long datasourceId, string accountFrom, string accountTo, int period, int year, string balanceType);
    Task<List<GlAccount>> GetChartOfAccountsAsync(long datasourceId, string? filter = null);
    Task<List<FiscalPeriod>> GetFiscalPeriodsAsync(long datasourceId, int year);
    Task<GlTableMapping?> DetectTablesAsync(long datasourceId);
    Task<ErpConnectorConfig?> GetConfigAsync(long datasourceId);
    Task SaveConfigAsync(long datasourceId, ErpConnectorConfig config);
}

public class GlFormulaProcessor : IGlFormulaProcessor
{
    private readonly IDatabase _database;
    private readonly IConnectionFactory _connectionFactory;
    private readonly IDataProviderRegistry _providerRegistry;
    private readonly IRedisCache _redisCache;
    private readonly ILogger<GlFormulaProcessor> _logger;

    private readonly AccountSegmentParser _segmentParser = new();
    private readonly PeriodResolver _periodResolver = new();
    private readonly GlTableDetector _tableDetector = new();

    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);
    private static readonly string[] AllowedBalanceTypes = { "PTD", "YTD", "QTD", "BAL", "BEG" };

    public GlFormulaProcessor(
        IDatabase database,
        IConnectionFactory connectionFactory,
        IDataProviderRegistry providerRegistry,
        IRedisCache redisCache,
        ILogger<GlFormulaProcessor> logger)
    {
        _database = database;
        _connectionFactory = connectionFactory;
        _providerRegistry = providerRegistry;
        _redisCache = redisCache;
        _logger = logger;
    }

    // ── Account Balance ──────────────────────────────────────────────────────

    public async Task<decimal?> GetAccountBalanceAsync(
        long datasourceId, string account, int period, int year, string balanceType)
    {
        var balType = (balanceType ?? "YTD").Trim().ToUpperInvariant();
        if (!AllowedBalanceTypes.Contains(balType))
            throw new ArgumentException($"Invalid balance type: {balType}");

        // Check cache
        var cacheKey = BuildCacheKey(datasourceId, account, period, year, balType);
        var cached = await _redisCache.GetAsync<CachedDecimal>(cacheKey);
        if (cached != null)
            return cached.Value;

        var config = await LoadConfigWithFallbackAsync(datasourceId);
        if (config == null)
            throw new InvalidOperationException($"GL connector not configured for datasource {datasourceId} and auto-detection failed.");

        decimal? result;

        if (!string.IsNullOrWhiteSpace(config.GlBalanceQuery))
        {
            // Use configured query template
            result = await ExecuteBalanceQueryAsync(datasourceId, config, account, period, year, balType);
        }
        else
        {
            // Build query dynamically from table mapping
            result = await ExecuteDynamicBalanceAsync(datasourceId, config, account, period, year, balType);
        }

        await _redisCache.SetAsync(cacheKey, new CachedDecimal { Value = result }, CacheExpiry);
        return result;
    }

    // ── Account Detail ───────────────────────────────────────────────────────

    public async Task<List<Dictionary<string, object?>>> GetAccountDetailAsync(
        long datasourceId, string account, int period, int year, int maxRows = 1000)
    {
        var config = await LoadConfigWithFallbackAsync(datasourceId);
        if (config == null)
            throw new InvalidOperationException($"GL connector not configured for datasource {datasourceId}.");

        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);

        if (!string.IsNullOrWhiteSpace(config.GlDetailQuery))
        {
            var sql = config.GlDetailQuery;
            var rows = await conn.QueryAsync(sql, new
            {
                glAccount = account,
                glPeriod = period,
                glYear = year,
                maxRows
            }, commandTimeout: 30);

            return rows.Select(r => (IDictionary<string, object>)r)
                       .Select(r => r.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                       .Take(maxRows)
                       .ToList();
        }

        // Dynamic query from detected mapping
        var mapping = ParseMappingFromConfig(config);
        if (mapping?.DetailTable == null)
            throw new InvalidOperationException("No GL detail table configured or detected.");

        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        var provider = info.Provider;

        var acctCol = provider.QuoteIdentifier(mapping.AccountColumn ?? "ACCOUNT");
        var periodCol = provider.QuoteIdentifier(mapping.PeriodColumn ?? "PERIOD");
        var yearCol = provider.QuoteIdentifier(mapping.YearColumn ?? "FISCAL_YEAR");
        var table = provider.QuoteIdentifier(mapping.DetailTable);

        var dynamicSql = $"SELECT * FROM {table} WHERE {acctCol} = @glAccount AND {periodCol} = @glPeriod AND {yearCol} = @glYear";

        // Limit rows per provider
        dynamicSql = AppendLimit(dynamicSql, maxRows, info.ProviderType);

        var detailRows = await conn.QueryAsync(dynamicSql, new
        {
            glAccount = account,
            glPeriod = period,
            glYear = year
        }, commandTimeout: 30);

        return detailRows.Select(r => (IDictionary<string, object>)r)
                         .Select(r => r.ToDictionary(kv => kv.Key, kv => (object?)kv.Value))
                         .ToList();
    }

    // ── Account Range ────────────────────────────────────────────────────────

    public async Task<decimal?> GetAccountRangeAsync(
        long datasourceId, string accountFrom, string accountTo, int period, int year, string balanceType)
    {
        var balType = (balanceType ?? "YTD").Trim().ToUpperInvariant();
        if (!AllowedBalanceTypes.Contains(balType))
            throw new ArgumentException($"Invalid balance type: {balType}");

        var cacheKey = BuildCacheKey(datasourceId, $"{accountFrom}..{accountTo}", period, year, balType);
        var cached = await _redisCache.GetAsync<CachedDecimal>(cacheKey);
        if (cached != null)
            return cached.Value;

        var config = await LoadConfigWithFallbackAsync(datasourceId);
        if (config == null)
            throw new InvalidOperationException($"GL connector not configured for datasource {datasourceId}.");

        decimal? result;

        if (!string.IsNullOrWhiteSpace(config.GlRangeQuery))
        {
            result = await ExecuteRangeQueryAsync(datasourceId, config, accountFrom, accountTo, period, year, balType);
        }
        else
        {
            result = await ExecuteDynamicRangeAsync(datasourceId, config, accountFrom, accountTo, period, year, balType);
        }

        await _redisCache.SetAsync(cacheKey, new CachedDecimal { Value = result }, CacheExpiry);
        return result;
    }

    // ── Chart of Accounts ────────────────────────────────────────────────────

    public async Task<List<GlAccount>> GetChartOfAccountsAsync(long datasourceId, string? filter = null)
    {
        var config = await LoadConfigWithFallbackAsync(datasourceId);
        if (config == null)
            return new List<GlAccount>();

        var mapping = ParseMappingFromConfig(config);
        var accountTable = config.AccountTable ?? mapping?.AccountTable;
        if (string.IsNullOrWhiteSpace(accountTable))
            return new List<GlAccount>();

        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);
        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        var provider = info.Provider;

        var table = provider.QuoteIdentifier(accountTable);
        var sql = $"SELECT * FROM {table}";

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var acctCol = provider.QuoteIdentifier(mapping?.AccountColumn ?? config.AccountColumn ?? "ACCOUNT");
            sql += $" WHERE {acctCol} LIKE @filter";
        }

        sql = AppendLimit(sql, 5000, info.ProviderType);

        var rows = await conn.QueryAsync(sql, new { filter = $"%{filter}%" }, commandTimeout: 30);

        var accounts = new List<GlAccount>();
        foreach (IDictionary<string, object> row in rows)
        {
            var acct = new GlAccount();

            // Try to map columns heuristically
            acct.AccountNumber = FindColumnValue(row, "ACCOUNT", "ACCT_NO", "ACCOUNT_NO", "ACCOUNT_NUMBER", "ACCT_CODE", "ACCOUNT_CODE") ?? "";
            acct.AccountDescription = FindColumnValue(row, "DESCRIPTION", "ACCT_DESC", "ACCOUNT_DESC", "ACCOUNT_DESCRIPTION", "NAME", "ACCT_NAME");
            acct.AccountType = FindColumnValue(row, "ACCOUNT_TYPE", "ACCT_TYPE", "TYPE");
            acct.AccountCategory = FindColumnValue(row, "CATEGORY", "ACCT_CATEGORY", "GROUP", "ACCT_GROUP");

            if (!string.IsNullOrWhiteSpace(config.SegmentFormat) && !string.IsNullOrWhiteSpace(acct.AccountNumber))
            {
                acct.Segments = _segmentParser.Parse(acct.AccountNumber, config.SegmentFormat);
            }

            accounts.Add(acct);
        }

        return accounts;
    }

    // ── Fiscal Periods ───────────────────────────────────────────────────────

    public async Task<List<FiscalPeriod>> GetFiscalPeriodsAsync(long datasourceId, int year)
    {
        var config = await LoadConfigWithFallbackAsync(datasourceId);
        if (config == null)
            return GenerateDefaultPeriods(year, 1);

        var mapping = ParseMappingFromConfig(config);
        var periodTable = config.PeriodTable ?? mapping?.PeriodTable;

        if (string.IsNullOrWhiteSpace(periodTable))
        {
            // No period table -- generate default periods
            return GenerateDefaultPeriods(year, config.FiscalYearStartMonth);
        }

        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);
        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        var provider = info.Provider;

        var table = provider.QuoteIdentifier(periodTable);
        var yearCol = provider.QuoteIdentifier(mapping?.YearColumn ?? "FISCAL_YEAR");
        var sql = $"SELECT * FROM {table} WHERE {yearCol} = @year ORDER BY 1";

        var rows = await conn.QueryAsync(sql, new { year }, commandTimeout: 30);

        var periods = new List<FiscalPeriod>();
        foreach (IDictionary<string, object> row in rows)
        {
            var fp = new FiscalPeriod
            {
                FiscalYear = year,
            };

            var periodStr = FindColumnValue(row, "PERIOD", "PERIOD_NO", "PERIOD_NUMBER", "FISCAL_PERIOD");
            if (int.TryParse(periodStr, out var pNum))
                fp.PeriodNumber = pNum;

            fp.PeriodName = FindColumnValue(row, "PERIOD_NAME", "NAME", "DESCRIPTION");

            var startStr = FindColumnValue(row, "START_DATE", "BEGIN_DATE", "PERIOD_START");
            if (DateTime.TryParse(startStr, out var startDt))
                fp.StartDate = startDt;

            var endStr = FindColumnValue(row, "END_DATE", "PERIOD_END", "CLOSE_DATE");
            if (DateTime.TryParse(endStr, out var endDt))
                fp.EndDate = endDt;

            var closedStr = FindColumnValue(row, "IS_CLOSED", "CLOSED", "STATUS");
            fp.IsClosed = closedStr != null &&
                (closedStr.Equals("1") || closedStr.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 closedStr.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
                 closedStr.Equals("CLOSED", StringComparison.OrdinalIgnoreCase));

            periods.Add(fp);
        }

        return periods;
    }

    // ── Config Management ────────────────────────────────────────────────────

    public async Task<GlTableMapping?> DetectTablesAsync(long datasourceId)
    {
        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        using var conn = await info.Provider.CreateConnectionAsync(info.ConnectionString);
        return await _tableDetector.DetectAsync(conn, info.Provider);
    }

    public async Task<ErpConnectorConfig?> GetConfigAsync(long datasourceId)
    {
        return await LoadErpConfigFromDbAsync(datasourceId);
    }

    public async Task SaveConfigAsync(long datasourceId, ErpConnectorConfig config)
    {
        using var conn = await _database.GetConnectionAsync();

        var existing = await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM RS_ERP_CONNECTOR_CONFIG WHERE datasource_id = @DatasourceId",
            new { DatasourceId = datasourceId });

        config.DatasourceId = datasourceId;

        if (existing.HasValue)
        {
            await conn.ExecuteAsync(
                @"UPDATE RS_ERP_CONNECTOR_CONFIG SET
                    erp_type = @ErpType,
                    gl_balance_query = @GlBalanceQuery,
                    gl_detail_query = @GlDetailQuery,
                    gl_range_query = @GlRangeQuery,
                    account_table = @AccountTable,
                    account_column = @AccountColumn,
                    period_table = @PeriodTable,
                    fiscal_year_start_month = @FiscalYearStartMonth,
                    segment_format = @SegmentFormat,
                    config = @Config
                  WHERE datasource_id = @DatasourceId",
                config);
        }
        else
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_ERP_CONNECTOR_CONFIG
                    (datasource_id, erp_type, gl_balance_query, gl_detail_query, gl_range_query,
                     account_table, account_column, period_table, fiscal_year_start_month,
                     segment_format, config)
                  VALUES
                    (@DatasourceId, @ErpType, @GlBalanceQuery, @GlDetailQuery, @GlRangeQuery,
                     @AccountTable, @AccountColumn, @PeriodTable, @FiscalYearStartMonth,
                     @SegmentFormat, @Config)",
                config);
        }

        // Invalidate cache
        await _redisCache.RemoveByPatternAsync($"gl:{datasourceId}:*");
    }

    // ── Private: Query Execution ─────────────────────────────────────────────

    private async Task<decimal?> ExecuteBalanceQueryAsync(
        long datasourceId, ErpConnectorConfig config,
        string account, int period, int year, string balType)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);

        var sql = config.GlBalanceQuery!;
        var periods = _periodResolver.GetPeriodsForBalanceType(balType, period, year);

        // If the query template has {BalanceType}, replace inline; otherwise sum across periods
        if (sql.Contains("{BalanceType}", StringComparison.OrdinalIgnoreCase))
        {
            sql = sql
                .Replace("{Account}", "@glAccount")
                .Replace("{Period}", "@glPeriod")
                .Replace("{Year}", "@glYear")
                .Replace("{BalanceType}", "@glBalType");

            var value = await conn.ExecuteScalarAsync<decimal?>(sql, new
            {
                glAccount = account,
                glPeriod = period,
                glYear = year,
                glBalType = balType
            }, commandTimeout: 30);

            return value;
        }
        else
        {
            // Sum across resolved periods
            sql = sql
                .Replace("{Account}", "@glAccount")
                .Replace("{Period}", "@glPeriod")
                .Replace("{Year}", "@glYear");

            decimal total = 0;
            foreach (var p in periods)
            {
                var value = await conn.ExecuteScalarAsync<decimal?>(sql, new
                {
                    glAccount = account,
                    glPeriod = p,
                    glYear = year
                }, commandTimeout: 30);

                total += value ?? 0;
            }
            return total;
        }
    }

    private async Task<decimal?> ExecuteDynamicBalanceAsync(
        long datasourceId, ErpConnectorConfig config,
        string account, int period, int year, string balType)
    {
        var mapping = ParseMappingFromConfig(config);
        if (mapping?.BalanceTable == null && mapping?.DetailTable == null)
            throw new InvalidOperationException("No balance or detail table available for dynamic balance query.");

        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);
        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        var provider = info.Provider;

        var periods = _periodResolver.GetPeriodsForBalanceType(balType, period, year);

        var table = mapping.BalanceTable ?? mapping.DetailTable!;
        var acctCol = provider.QuoteIdentifier(mapping.AccountColumn ?? "ACCOUNT");
        var periodCol = provider.QuoteIdentifier(mapping.PeriodColumn ?? "PERIOD");
        var yearCol = provider.QuoteIdentifier(mapping.YearColumn ?? "FISCAL_YEAR");

        string amountExpr;
        if (mapping.AmountColumn != null)
        {
            amountExpr = $"SUM({provider.QuoteIdentifier(mapping.AmountColumn)})";
        }
        else if (mapping.DebitColumn != null && mapping.CreditColumn != null)
        {
            amountExpr = $"SUM({provider.QuoteIdentifier(mapping.DebitColumn)} - {provider.QuoteIdentifier(mapping.CreditColumn)})";
        }
        else
        {
            throw new InvalidOperationException("No amount, debit, or credit columns detected.");
        }

        var sql = $@"SELECT {amountExpr}
                     FROM {provider.QuoteIdentifier(table)}
                     WHERE {acctCol} = @glAccount
                       AND {yearCol} = @glYear
                       AND {periodCol} IN @glPeriods";

        var result = await conn.ExecuteScalarAsync<decimal?>(sql, new
        {
            glAccount = account,
            glYear = year,
            glPeriods = periods
        }, commandTimeout: 30);

        return result;
    }

    private async Task<decimal?> ExecuteRangeQueryAsync(
        long datasourceId, ErpConnectorConfig config,
        string accountFrom, string accountTo, int period, int year, string balType)
    {
        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);

        var sql = config.GlRangeQuery!
            .Replace("{AccountFrom}", "@glAccountFrom")
            .Replace("{AccountTo}", "@glAccountTo")
            .Replace("{Period}", "@glPeriod")
            .Replace("{Year}", "@glYear")
            .Replace("{BalanceType}", "@glBalType");

        var value = await conn.ExecuteScalarAsync<decimal?>(sql, new
        {
            glAccountFrom = accountFrom,
            glAccountTo = accountTo,
            glPeriod = period,
            glYear = year,
            glBalType = balType
        }, commandTimeout: 30);

        return value;
    }

    private async Task<decimal?> ExecuteDynamicRangeAsync(
        long datasourceId, ErpConnectorConfig config,
        string accountFrom, string accountTo, int period, int year, string balType)
    {
        var mapping = ParseMappingFromConfig(config);
        if (mapping?.BalanceTable == null && mapping?.DetailTable == null)
            throw new InvalidOperationException("No balance or detail table available for dynamic range query.");

        using var conn = await _connectionFactory.CreateConnectionAsync(datasourceId);
        var info = await _connectionFactory.GetConnectionInfoAsync(datasourceId);
        var provider = info.Provider;

        var periods = _periodResolver.GetPeriodsForBalanceType(balType, period, year);

        var table = mapping.BalanceTable ?? mapping.DetailTable!;
        var acctCol = provider.QuoteIdentifier(mapping.AccountColumn ?? "ACCOUNT");
        var periodCol = provider.QuoteIdentifier(mapping.PeriodColumn ?? "PERIOD");
        var yearCol = provider.QuoteIdentifier(mapping.YearColumn ?? "FISCAL_YEAR");

        string amountExpr;
        if (mapping.AmountColumn != null)
        {
            amountExpr = $"SUM({provider.QuoteIdentifier(mapping.AmountColumn)})";
        }
        else if (mapping.DebitColumn != null && mapping.CreditColumn != null)
        {
            amountExpr = $"SUM({provider.QuoteIdentifier(mapping.DebitColumn)} - {provider.QuoteIdentifier(mapping.CreditColumn)})";
        }
        else
        {
            throw new InvalidOperationException("No amount, debit, or credit columns detected.");
        }

        var sql = $@"SELECT {amountExpr}
                     FROM {provider.QuoteIdentifier(table)}
                     WHERE {acctCol} >= @glAccountFrom
                       AND {acctCol} <= @glAccountTo
                       AND {yearCol} = @glYear
                       AND {periodCol} IN @glPeriods";

        var result = await conn.ExecuteScalarAsync<decimal?>(sql, new
        {
            glAccountFrom = accountFrom,
            glAccountTo = accountTo,
            glYear = year,
            glPeriods = periods
        }, commandTimeout: 30);

        return result;
    }

    // ── Private: Config Loading ──────────────────────────────────────────────

    private async Task<ErpConnectorConfig?> LoadConfigWithFallbackAsync(long datasourceId)
    {
        // Try loading explicit config first
        var config = await LoadErpConfigFromDbAsync(datasourceId);
        if (config != null)
            return config;

        // No config exists -- try auto-detecting GL tables
        _logger.LogInformation("No ERP config for datasource {DatasourceId}, attempting auto-detection", datasourceId);

        try
        {
            var mapping = await DetectTablesAsync(datasourceId);
            if (mapping == null || mapping.Confidence < 0.25f)
            {
                _logger.LogWarning("GL table auto-detection failed or low confidence ({Confidence}) for datasource {DatasourceId}",
                    mapping?.Confidence ?? 0, datasourceId);
                return null;
            }

            // Create a synthetic config from the detected mapping
            config = new ErpConnectorConfig
            {
                DatasourceId = datasourceId,
                ErpType = "AUTO_DETECTED",
                AccountTable = mapping.AccountTable,
                AccountColumn = mapping.AccountColumn,
                PeriodTable = mapping.PeriodTable,
                FiscalYearStartMonth = 1,
                Config = JsonSerializer.Serialize(mapping)
            };

            // Save the auto-detected config for future use
            await SaveConfigAsync(datasourceId, config);
            _logger.LogInformation(
                "Auto-detected GL tables for datasource {DatasourceId} with confidence {Confidence}: balance={Balance}, detail={Detail}, account={Account}",
                datasourceId, mapping.Confidence, mapping.BalanceTable, mapping.DetailTable, mapping.AccountTable);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GL table auto-detection threw for datasource {DatasourceId}", datasourceId);
            return null;
        }
    }

    private async Task<ErpConnectorConfig?> LoadErpConfigFromDbAsync(long datasourceId)
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

    private static GlTableMapping? ParseMappingFromConfig(ErpConnectorConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Config))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GlTableMapping>(config.Config, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    // ── Private: Helpers ─────────────────────────────────────────────────────

    private static string BuildCacheKey(long datasourceId, string account, int period, int year, string balType)
    {
        var raw = $"gl:{datasourceId}:{account}:{period}:{year}:{balType}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"gl:{datasourceId}:{Convert.ToHexStringLower(hash)[..16]}";
    }

    private static string AppendLimit(string sql, int limit, DataProviderType providerType)
    {
        return providerType switch
        {
            DataProviderType.SqlServer => $"SELECT TOP {limit} * FROM ({sql}) AS _limited",
            _ => $"{sql} LIMIT {limit}"
        };
    }

    private static string? FindColumnValue(IDictionary<string, object> row, params string[] candidateNames)
    {
        foreach (var candidate in candidateNames)
        {
            // Try exact match first
            if (row.TryGetValue(candidate, out var val) && val != null && val != DBNull.Value)
                return val.ToString();

            // Try case-insensitive
            var match = row.Keys.FirstOrDefault(k => k.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null && row[match] != null && row[match] != DBNull.Value)
                return row[match]!.ToString();
        }
        return null;
    }

    private static List<FiscalPeriod> GenerateDefaultPeriods(int year, int fiscalYearStartMonth)
    {
        var resolver = new PeriodResolver();
        var periods = new List<FiscalPeriod>();

        for (int p = 0; p <= 12; p++)
        {
            var fp = new FiscalPeriod
            {
                PeriodNumber = p,
                FiscalYear = year,
                PeriodName = p == 0 ? "Beginning Balance" : $"Period {p}"
            };

            try
            {
                var (start, end) = resolver.GetPeriodDateRange(p, year, fiscalYearStartMonth);
                fp.StartDate = start;
                fp.EndDate = end;
            }
            catch
            {
                // Period 0 or other edge cases -- leave dates null
            }

            periods.Add(fp);
        }

        return periods;
    }

    /// <summary>
    /// Helper class for caching nullable decimal values via Redis (which requires a class type).
    /// </summary>
    private class CachedDecimal
    {
        public decimal? Value { get; set; }
    }
}
