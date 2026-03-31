using System.Text.RegularExpressions;
using DaisyReport.Api.SpreadsheetServer.Models;
using DaisyReport.Api.SpreadsheetServer.Services;

namespace DaisyReport.Api.SpreadsheetServer.Distribution;

public class FormulaResolver
{
    private readonly ISpreadsheetQueryService _queryService;
    private readonly ILogger<FormulaResolver> _logger;

    // System user ID for distribution engine operations
    private const long SystemUserId = 0;

    public FormulaResolver(ISpreadsheetQueryService queryService, ILogger<FormulaResolver> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    /// <summary>
    /// Parse an Excel formula string and extract function name + arguments.
    /// Handles: =DSQUERY("conn", "sql", 1000), =GXLA("conn", "1000-100", 3, 2026, "YTD")
    /// </summary>
    public (string FunctionName, List<string> Arguments) ParseFormula(string formula)
    {
        // Strip leading = sign
        var trimmed = formula.TrimStart('=').Trim();

        // Find the function name (everything before the first parenthesis)
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx < 0)
            return (trimmed.ToUpperInvariant(), new List<string>());

        var functionName = trimmed[..parenIdx].Trim().ToUpperInvariant();
        var argsStr = trimmed[(parenIdx + 1)..].TrimEnd(')').Trim();

        var arguments = ParseArguments(argsStr);
        return (functionName, arguments);
    }

    /// <summary>
    /// Resolve a formula to its result value using the SpreadsheetQueryService.
    /// </summary>
    public async Task<object?> ResolveAsync(string formula, long defaultConnectionId)
    {
        var (funcName, args) = ParseFormula(formula);

        return funcName switch
        {
            "DSQUERY" => await ResolveDsQueryAsync(args, defaultConnectionId),
            "DSSUM" => await ResolveDsAggregateAsync(args, defaultConnectionId, "SUM"),
            "DSAVG" => await ResolveDsAggregateAsync(args, defaultConnectionId, "AVG"),
            "DSCOUNT" => await ResolveDsAggregateAsync(args, defaultConnectionId, "COUNT"),
            "DSMIN" => await ResolveDsAggregateAsync(args, defaultConnectionId, "MIN"),
            "DSMAX" => await ResolveDsAggregateAsync(args, defaultConnectionId, "MAX"),
            "DSLOOKUP" => await ResolveDsLookupAsync(args, defaultConnectionId),
            "GXLA" or "GXLBALANCE" => await ResolveGlBalanceAsync(args, defaultConnectionId),
            "GXLD" or "GXLDETAIL" => await ResolveGlDetailScalarAsync(args, defaultConnectionId),
            "GXLR" or "GXLRANGE" => await ResolveGlRangeAsync(args, defaultConnectionId),
            _ => throw new InvalidOperationException($"Unknown formula function: {funcName}")
        };
    }

    // ── DSQUERY ──────────────────────────────────────────────────────────────────
    // =DSQUERY("ConnectionNameOrId", "SELECT * FROM orders", maxRows)
    // Returns the value of the first cell (row 0, col 0) for scalar use in Excel

    private async Task<object?> ResolveDsQueryAsync(List<string> args, long defaultConnectionId)
    {
        if (args.Count < 2)
            throw new ArgumentException("DSQUERY requires at least 2 arguments: connectionId, query");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var queryOrSql = args[1];
        var maxRows = args.Count > 2 && int.TryParse(args[2], out var mr) ? mr : 1;

        var request = new QueryRequest
        {
            ConnectionId = connectionId,
            QueryNameOrSql = queryOrSql,
            MaxRows = maxRows
        };

        var result = await _queryService.ExecuteQueryAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"DSQUERY error: {result.Error}");

        // Return first cell value for scalar formula usage
        if (result.Rows.Count > 0 && result.Rows[0].Length > 0)
            return result.Rows[0][0];

        return null;
    }

    // ── DS Aggregate (SUM/AVG/COUNT/MIN/MAX) ─────────────────────────────────────
    // =DSSUM("ConnectionNameOrId", "SELECT amount FROM orders", "amount")

    private async Task<object?> ResolveDsAggregateAsync(List<string> args, long defaultConnectionId, string function)
    {
        if (args.Count < 3)
            throw new ArgumentException($"DS{function} requires 3 arguments: connectionId, query, column");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var queryOrSql = args[1];
        var column = args[2];

        var request = new AggregateRequest
        {
            ConnectionId = connectionId,
            QueryNameOrSql = queryOrSql,
            AggregateColumn = column,
            AggregateFunction = function
        };

        var result = await _queryService.ExecuteAggregateAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"DS{function} error: {result.Error}");

        return result.Value;
    }

    // ── DSLOOKUP ─────────────────────────────────────────────────────────────────
    // =DSLOOKUP("ConnectionNameOrId", "query", "returnCol", "lookupCol", "lookupVal")

    private async Task<object?> ResolveDsLookupAsync(List<string> args, long defaultConnectionId)
    {
        if (args.Count < 5)
            throw new ArgumentException("DSLOOKUP requires 5 arguments: connectionId, query, returnColumn, lookupColumn, lookupValue");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var queryOrSql = args[1];
        var returnColumn = args[2];
        var lookupColumn = args[3];
        var lookupValue = args[4];

        var request = new LookupRequest
        {
            ConnectionId = connectionId,
            QueryNameOrSql = queryOrSql,
            ReturnColumn = returnColumn,
            LookupColumn = lookupColumn,
            LookupValue = lookupValue
        };

        var result = await _queryService.ExecuteLookupAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"DSLOOKUP error: {result.Error}");

        return result.Value;
    }

    // ── GXLA / GXLBALANCE ────────────────────────────────────────────────────────
    // =GXLA("ConnectionNameOrId", "1000-100", period, year, "YTD")

    private async Task<object?> ResolveGlBalanceAsync(List<string> args, long defaultConnectionId)
    {
        if (args.Count < 5)
            throw new ArgumentException("GXLA requires 5 arguments: connectionId, account, period, year, balanceType");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var account = args[1];
        if (!int.TryParse(args[2], out var period))
            throw new ArgumentException($"GXLA period must be an integer, got: {args[2]}");
        if (!int.TryParse(args[3], out var year))
            throw new ArgumentException($"GXLA year must be an integer, got: {args[3]}");
        var balanceType = args[4];

        var request = new GlBalanceRequest
        {
            ConnectionId = connectionId,
            Account = account,
            Period = period,
            Year = year,
            BalanceType = balanceType
        };

        var result = await _queryService.GetGlBalanceAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"GXLA error: {result.Error}");

        return result.Value;
    }

    // ── GXLD / GXLDETAIL ─────────────────────────────────────────────────────────
    // =GXLD("ConnectionNameOrId", "1000-100", period, year)
    // Returns the first value from the GL detail query

    private async Task<object?> ResolveGlDetailScalarAsync(List<string> args, long defaultConnectionId)
    {
        if (args.Count < 4)
            throw new ArgumentException("GXLD requires 4 arguments: connectionId, account, period, year");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var account = args[1];
        if (!int.TryParse(args[2], out var period))
            throw new ArgumentException($"GXLD period must be an integer, got: {args[2]}");
        if (!int.TryParse(args[3], out var year))
            throw new ArgumentException($"GXLD year must be an integer, got: {args[3]}");

        var request = new GlDetailRequest
        {
            ConnectionId = connectionId,
            Account = account,
            Period = period,
            Year = year,
            MaxRows = 1
        };

        var result = await _queryService.GetGlDetailAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"GXLD error: {result.Error}");

        if (result.Rows.Count > 0 && result.Rows[0].Length > 0)
            return result.Rows[0][0];

        return null;
    }

    // ── GXLR / GXLRANGE ─────────────────────────────────────────────────────────
    // =GXLR("ConnectionNameOrId", "1000-000", "1000-999", period, year, "YTD")

    private async Task<object?> ResolveGlRangeAsync(List<string> args, long defaultConnectionId)
    {
        if (args.Count < 6)
            throw new ArgumentException("GXLR requires 6 arguments: connectionId, accountFrom, accountTo, period, year, balanceType");

        var connectionId = ResolveConnectionId(args[0], defaultConnectionId);
        var accountFrom = args[1];
        var accountTo = args[2];
        if (!int.TryParse(args[3], out var period))
            throw new ArgumentException($"GXLR period must be an integer, got: {args[3]}");
        if (!int.TryParse(args[4], out var year))
            throw new ArgumentException($"GXLR year must be an integer, got: {args[4]}");
        var balanceType = args[5];

        var request = new GlRangeRequest
        {
            ConnectionId = connectionId,
            AccountFrom = accountFrom,
            AccountTo = accountTo,
            Period = period,
            Year = year,
            BalanceType = balanceType
        };

        var result = await _queryService.GetGlRangeAsync(request, SystemUserId);
        if (!string.IsNullOrEmpty(result.Error))
            throw new InvalidOperationException($"GXLR error: {result.Error}");

        return result.Value;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve a connection argument: if it's a number use as-is, otherwise fall back to default.
    /// </summary>
    private static long ResolveConnectionId(string arg, long defaultConnectionId)
    {
        if (long.TryParse(arg, out var id))
            return id;

        // If the arg is a named connection, use default (the engine should resolve names
        // in a future version; for now we use the default connection from the template)
        return defaultConnectionId;
    }

    /// <summary>
    /// Parse a comma-separated argument string, respecting quoted strings.
    /// </summary>
    private static List<string> ParseArguments(string argsStr)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(argsStr))
            return arguments;

        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        char quoteChar = '"';
        int depth = 0; // Track nested parentheses

        for (int i = 0; i < argsStr.Length; i++)
        {
            char c = argsStr[i];

            if (inQuotes)
            {
                if (c == quoteChar)
                {
                    // Check for escaped quote (double quote)
                    if (i + 1 < argsStr.Length && argsStr[i + 1] == quoteChar)
                    {
                        current.Append(c);
                        i++; // Skip next quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"' || c == '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                }
                else if (c == '(')
                {
                    depth++;
                    current.Append(c);
                }
                else if (c == ')')
                {
                    depth--;
                    current.Append(c);
                }
                else if (c == ',' && depth == 0)
                {
                    arguments.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        // Add the last argument
        var last = current.ToString().Trim();
        if (last.Length > 0)
            arguments.Add(last);

        return arguments;
    }
}
