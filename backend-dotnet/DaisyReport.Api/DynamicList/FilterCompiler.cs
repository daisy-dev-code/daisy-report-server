using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DaisyReport.Api.DynamicList;

// ── Filter Definition ──────────────────────────────────────────────────────────

public class FilterDefinition
{
    public string ColumnName { get; set; } = "";
    public string FilterType { get; set; } = ""; // INCLUSION, EXCLUSION, RANGE, WILDCARD, NULL, REGEX, PREFILTER, HAVING
    public List<string> Values { get; set; } = new();
    public bool CaseInsensitive { get; set; }
    public bool IsNullIncluded { get; set; }

    /// <summary>
    /// JSON tree for PREFILTER type: { "type": "AND_BLOCK", "children": [...] }
    /// </summary>
    public string? TreeJson { get; set; }
}

// ── Filter Compiler ────────────────────────────────────────────────────────────

public class FilterCompiler
{
    private const double FloatEpsilon = 0.0001;

    /// <summary>
    /// Compiles a single filter into a parameterized SQL condition.
    /// </summary>
    public (string Sql, List<object> Parameters) CompileFilter(FilterDefinition filter, string columnRef)
    {
        var effectiveRef = filter.CaseInsensitive ? $"UPPER({columnRef})" : columnRef;

        return filter.FilterType.ToUpperInvariant() switch
        {
            "INCLUSION" => CompileInclusion(effectiveRef, filter),
            "EXCLUSION" => CompileExclusion(effectiveRef, filter),
            "RANGE" => CompileRange(columnRef, filter), // range uses raw column ref
            "WILDCARD" => CompileWildcard(effectiveRef, filter),
            "NULL" => CompileNull(columnRef, filter),
            "REGEX" => CompileRegex(columnRef, filter),
            "HAVING" => CompileInclusion(effectiveRef, filter), // reuse inclusion logic for HAVING
            _ => ("", new List<object>())
        };
    }

    /// <summary>
    /// Compiles a PREFILTER tree (AND_BLOCK / OR_BLOCK / CONDITION nodes).
    /// </summary>
    public (string Sql, List<object> Parameters) CompilePrefilter(FilterDefinition filter)
    {
        if (string.IsNullOrWhiteSpace(filter.TreeJson))
            return ("", new List<object>());

        try
        {
            var node = JsonSerializer.Deserialize<PrefilterNode>(filter.TreeJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (node == null)
                return ("", new List<object>());

            return CompilePrefilterNode(node);
        }
        catch
        {
            return ("", new List<object>());
        }
    }

    // ── INCLUSION: column IN (?, ?, ?) ─────────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileInclusion(
        string columnRef, FilterDefinition filter)
    {
        if (filter.Values.Count == 0 && !filter.IsNullIncluded)
            return ("", new List<object>());

        var parameters = new List<object>();
        var conditions = new List<string>();

        if (filter.Values.Count > 0)
        {
            var placeholders = new List<string>();
            foreach (var val in filter.Values)
            {
                var effectiveVal = filter.CaseInsensitive ? val.ToUpperInvariant() : val;
                placeholders.Add($"@p{{{parameters.Count}}}");
                parameters.Add(effectiveVal);
            }

            if (placeholders.Count == 1)
                conditions.Add($"{columnRef} = @p{{{parameters.Count - 1}}}");
            else
                conditions.Add($"{columnRef} IN ({string.Join(", ", placeholders)})");
        }

        if (filter.IsNullIncluded)
            conditions.Add($"{columnRef} IS NULL");

        var sql = conditions.Count == 1
            ? conditions[0]
            : $"({string.Join(" OR ", conditions)})";

        return (RewritePlaceholders(sql, parameters.Count), parameters);
    }

    // ── EXCLUSION: column NOT IN (?, ?, ?) ─────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileExclusion(
        string columnRef, FilterDefinition filter)
    {
        if (filter.Values.Count == 0 && !filter.IsNullIncluded)
            return ("", new List<object>());

        var parameters = new List<object>();
        var conditions = new List<string>();

        if (filter.Values.Count > 0)
        {
            var placeholders = new List<string>();
            foreach (var val in filter.Values)
            {
                var effectiveVal = filter.CaseInsensitive ? val.ToUpperInvariant() : val;
                placeholders.Add($"@p{{{parameters.Count}}}");
                parameters.Add(effectiveVal);
            }

            conditions.Add($"{columnRef} NOT IN ({string.Join(", ", placeholders)})");
        }

        if (filter.IsNullIncluded)
            conditions.Add($"{columnRef} IS NOT NULL");

        var sql = conditions.Count == 1
            ? conditions[0]
            : $"({string.Join(" AND ", conditions)})";

        return (RewritePlaceholders(sql, parameters.Count), parameters);
    }

    // ── RANGE: column BETWEEN ? AND ? ──────────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileRange(
        string columnRef, FilterDefinition filter)
    {
        if (filter.Values.Count == 0)
            return ("", new List<object>());

        var parameters = new List<object>();
        var conditions = new List<string>();

        foreach (var rangeStr in filter.Values)
        {
            var parts = rangeStr.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;

            var startStr = parts[0].Trim();
            var endStr = parts[1].Trim();

            // Try parse as numbers for epsilon adjustment
            if (double.TryParse(startStr, CultureInfo.InvariantCulture, out var startNum) &&
                double.TryParse(endStr, CultureInfo.InvariantCulture, out var endNum))
            {
                // Float precision: expand range by epsilon for small values
                if (Math.Abs(startNum) < 1.0) startNum -= FloatEpsilon;
                if (Math.Abs(endNum) < 1.0) endNum += FloatEpsilon;

                var startIdx = parameters.Count;
                parameters.Add(startNum);
                var endIdx = parameters.Count;
                parameters.Add(endNum);
                conditions.Add($"{columnRef} BETWEEN @p{startIdx} AND @p{endIdx}");
            }
            else
            {
                var startIdx = parameters.Count;
                parameters.Add(startStr);
                var endIdx = parameters.Count;
                parameters.Add(endStr);
                conditions.Add($"{columnRef} BETWEEN @p{startIdx} AND @p{endIdx}");
            }
        }

        if (conditions.Count == 0)
            return ("", new List<object>());

        var sql = conditions.Count == 1
            ? conditions[0]
            : $"({string.Join(" OR ", conditions)})";

        if (filter.IsNullIncluded)
            sql = $"({sql} OR {columnRef} IS NULL)";

        return (sql, parameters);
    }

    // ── WILDCARD: column LIKE ? ────────────────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileWildcard(
        string columnRef, FilterDefinition filter)
    {
        if (filter.Values.Count == 0)
            return ("", new List<object>());

        var parameters = new List<object>();
        var conditions = new List<string>();

        foreach (var pattern in filter.Values)
        {
            // First escape existing SQL LIKE special chars
            var escaped = pattern
                .Replace(@"\", @"\\")
                .Replace("%", @"\%")
                .Replace("_", @"\_");

            // Then translate user wildcards: * -> %, ? -> _
            escaped = escaped
                .Replace("*", "%")
                .Replace("?", "_");

            if (filter.CaseInsensitive)
                escaped = escaped.ToUpperInvariant();

            var idx = parameters.Count;
            parameters.Add(escaped);
            conditions.Add($"{columnRef} LIKE @p{idx} ESCAPE '\\'");
        }

        var sql = conditions.Count == 1
            ? conditions[0]
            : $"({string.Join(" OR ", conditions)})";

        if (filter.IsNullIncluded)
            sql = $"({sql} OR {columnRef} IS NULL)";

        return (sql, parameters);
    }

    // ── NULL: column IS NULL / IS NOT NULL ─────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileNull(
        string columnRef, FilterDefinition filter)
    {
        // Values: ["NULL"] = IS NULL, ["NOT NULL"] = IS NOT NULL, empty = IS NULL
        var isNull = filter.Values.Count == 0 ||
                     filter.Values.Any(v => string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase));

        var sql = isNull ? $"{columnRef} IS NULL" : $"{columnRef} IS NOT NULL";
        return (sql, new List<object>());
    }

    // ── REGEX: column REGEXP ? ─────────────────────────────────────────────────

    private static (string Sql, List<object> Parameters) CompileRegex(
        string columnRef, FilterDefinition filter)
    {
        if (filter.Values.Count == 0)
            return ("", new List<object>());

        var parameters = new List<object>();
        var conditions = new List<string>();

        foreach (var pattern in filter.Values)
        {
            var idx = parameters.Count;
            parameters.Add(pattern);
            conditions.Add($"{columnRef} REGEXP @p{idx}");
        }

        var sql = conditions.Count == 1
            ? conditions[0]
            : $"({string.Join(" OR ", conditions)})";

        if (filter.IsNullIncluded)
            sql = $"({sql} OR {columnRef} IS NULL)";

        return (sql, parameters);
    }

    // ── PREFILTER tree compilation ─────────────────────────────────────────────

    private (string Sql, List<object> Parameters) CompilePrefilterNode(PrefilterNode node)
    {
        switch (node.Type?.ToUpperInvariant())
        {
            case "AND_BLOCK":
                return CompileLogicBlock(node, "AND");

            case "OR_BLOCK":
                return CompileLogicBlock(node, "OR");

            case "CONDITION":
                return CompileConditionNode(node);

            default:
                return ("", new List<object>());
        }
    }

    private (string Sql, List<object> Parameters) CompileLogicBlock(PrefilterNode node, string logic)
    {
        if (node.Children == null || node.Children.Count == 0)
            return ("", new List<object>());

        var parts = new List<string>();
        var allParams = new List<object>();

        foreach (var child in node.Children)
        {
            var (sql, parms) = CompilePrefilterNode(child);
            if (!string.IsNullOrEmpty(sql))
            {
                // Rewrite parameter indices to be globally unique
                var rewritten = RewriteParamIndices(sql, allParams.Count, parms.Count);
                parts.Add(rewritten);
                allParams.AddRange(parms);
            }
        }

        if (parts.Count == 0)
            return ("", new List<object>());

        var combined = parts.Count == 1
            ? parts[0]
            : $"({string.Join($" {logic} ", parts)})";

        return (combined, allParams);
    }

    private (string Sql, List<object> Parameters) CompileConditionNode(PrefilterNode node)
    {
        if (string.IsNullOrEmpty(node.Column) || string.IsNullOrEmpty(node.Operator))
            return ("", new List<object>());

        var columnRef = $"_rs_base.`{node.Column}`";
        var filter = new FilterDefinition
        {
            ColumnName = node.Column,
            FilterType = MapOperatorToFilterType(node.Operator),
            Values = node.Value != null ? new List<string> { node.Value } : new List<string>(),
            CaseInsensitive = node.CaseInsensitive
        };

        return CompileFilter(filter, columnRef);
    }

    private static string MapOperatorToFilterType(string op)
    {
        return op.ToUpperInvariant() switch
        {
            "IN" or "EQUALS" or "=" => "INCLUSION",
            "NOT IN" or "NOT_EQUALS" or "!=" or "<>" => "EXCLUSION",
            "BETWEEN" or "RANGE" => "RANGE",
            "LIKE" or "CONTAINS" or "STARTS_WITH" or "ENDS_WITH" => "WILDCARD",
            "IS NULL" or "IS NOT NULL" => "NULL",
            "REGEXP" or "MATCHES" => "REGEX",
            _ => "INCLUSION"
        };
    }

    // ── Parameter placeholder helpers ──────────────────────────────────────────

    /// <summary>
    /// Rewrites {N} style placeholders in compiled SQL to proper @pN format.
    /// </summary>
    private static string RewritePlaceholders(string sql, int paramCount)
    {
        var result = sql;
        for (int i = 0; i < paramCount; i++)
        {
            result = result.Replace($"@p{{{i}}}", $"@p{i}");
        }
        return result;
    }

    /// <summary>
    /// Rewrites @pN indices in a fragment to @p(N+offset).
    /// </summary>
    private static string RewriteParamIndices(string sql, int offset, int paramCount)
    {
        if (offset == 0)
            return sql;

        var result = sql;
        // Replace in reverse order to avoid @p1 matching @p10
        for (int i = paramCount - 1; i >= 0; i--)
        {
            result = result.Replace($"@p{i}", $"@p{i + offset}");
        }
        return result;
    }
}

// ── Prefilter Tree Node ────────────────────────────────────────────────────────

public class PrefilterNode
{
    public string? Type { get; set; }       // AND_BLOCK, OR_BLOCK, CONDITION
    public List<PrefilterNode>? Children { get; set; }

    // CONDITION properties
    public string? Column { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
    public bool CaseInsensitive { get; set; }
}
