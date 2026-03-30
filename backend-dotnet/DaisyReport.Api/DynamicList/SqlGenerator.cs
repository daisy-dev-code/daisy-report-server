using System.Text;
using System.Text.RegularExpressions;

namespace DaisyReport.Api.DynamicList;

// ── Context passed to the generator ────────────────────────────────────────────

public class SqlGenerationContext
{
    public string QueryText { get; set; } = "";
    public List<ColumnSelection> Columns { get; set; } = new();
    public List<FilterDefinition>? Filters { get; set; }
    public List<SortDefinition>? Sorts { get; set; }
    public AggregationConfig? Aggregation { get; set; }
    public AggregationResult? AggregationResult { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public bool Distinct { get; set; }
}

// ── SQL Generator ──────────────────────────────────────────────────────────────

public partial class SqlGenerator
{
    private readonly FilterCompiler _filterCompiler;

    private static readonly Regex CteRegex = CtePattern();

    [GeneratedRegex(@"/\*<rs:cte>\*/\s*(.*?)\s*/\*</rs:cte>\*/", RegexOptions.Singleline)]
    private static partial Regex CtePattern();

    public SqlGenerator(FilterCompiler filterCompiler)
    {
        _filterCompiler = filterCompiler;
    }

    /// <summary>
    /// Generates the full data query with pagination.
    /// </summary>
    public (string Sql, List<object> Parameters) Generate(SqlGenerationContext context)
    {
        var parameters = new List<object>();
        var sb = new StringBuilder();

        // 1. CTE prefix
        var (cteClause, baseQuery) = ExtractCte(context.QueryText);
        if (!string.IsNullOrEmpty(cteClause))
            sb.Append(cteClause).Append(' ');

        // 2. SELECT
        sb.Append(BuildSelectClause(context.Columns, context.AggregationResult, context.Distinct));

        // 3. FROM
        sb.Append(BuildFromClause(baseQuery));

        // 4. WHERE
        var (whereClause, whereParams) = BuildWhereClause(context.Filters);
        if (!string.IsNullOrEmpty(whereClause))
        {
            sb.Append(" WHERE ").Append(whereClause);
            parameters.AddRange(whereParams);
        }

        // 5. GROUP BY
        if (context.AggregationResult != null)
        {
            var groupBy = BuildGroupByClause(context.AggregationResult);
            if (!string.IsNullOrEmpty(groupBy))
                sb.Append(groupBy);
        }

        // 6. HAVING
        if (context.Filters != null && context.AggregationResult != null)
        {
            var (havingClause, havingParams) = BuildHavingClause(context.Filters);
            if (!string.IsNullOrEmpty(havingClause))
            {
                sb.Append(" HAVING ").Append(havingClause);
                parameters.AddRange(havingParams);
            }
        }

        // 7. ORDER BY
        var orderBy = BuildOrderByClause(context.Sorts);
        if (!string.IsNullOrEmpty(orderBy))
            sb.Append(orderBy);

        // 8. LIMIT/OFFSET
        sb.Append(BuildLimitClause(context.Page, context.PageSize, parameters));

        return (sb.ToString(), parameters);
    }

    /// <summary>
    /// Generates a count query (no ORDER BY, no LIMIT).
    /// </summary>
    public (string Sql, List<object> Parameters) GenerateCount(SqlGenerationContext context)
    {
        var parameters = new List<object>();
        var sb = new StringBuilder();

        var (cteClause, baseQuery) = ExtractCte(context.QueryText);
        if (!string.IsNullOrEmpty(cteClause))
            sb.Append(cteClause).Append(' ');

        sb.Append("SELECT COUNT(*) FROM (");

        // Inner query
        var innerSb = new StringBuilder();
        innerSb.Append(BuildSelectClause(context.Columns, context.AggregationResult, context.Distinct));
        innerSb.Append(BuildFromClause(baseQuery));

        var (whereClause, whereParams) = BuildWhereClause(context.Filters);
        if (!string.IsNullOrEmpty(whereClause))
        {
            innerSb.Append(" WHERE ").Append(whereClause);
            parameters.AddRange(whereParams);
        }

        if (context.AggregationResult != null)
        {
            var groupBy = BuildGroupByClause(context.AggregationResult);
            if (!string.IsNullOrEmpty(groupBy))
                innerSb.Append(groupBy);
        }

        if (context.Filters != null && context.AggregationResult != null)
        {
            var (havingClause, havingParams) = BuildHavingClause(context.Filters);
            if (!string.IsNullOrEmpty(havingClause))
            {
                innerSb.Append(" HAVING ").Append(havingClause);
                parameters.AddRange(havingParams);
            }
        }

        sb.Append(innerSb);
        sb.Append(") AS _rs_count");

        return (sb.ToString(), parameters);
    }

    // ── Clause Builders ────────────────────────────────────────────────────────

    internal static string BuildSelectClause(
        List<ColumnSelection> columns, AggregationResult? aggregation, bool distinct)
    {
        var sb = new StringBuilder("SELECT ");
        if (distinct)
            sb.Append("DISTINCT ");

        if (aggregation != null && aggregation.SelectExpressions.Count > 0)
        {
            sb.Append(string.Join(", ", aggregation.SelectExpressions));
            return sb.ToString();
        }

        if (columns.Count == 0)
        {
            sb.Append("_rs_base.*");
            return sb.ToString();
        }

        var selectParts = new List<string>();
        foreach (var col in columns)
        {
            string expr;
            if (!string.IsNullOrEmpty(col.ComputedExpression))
            {
                expr = col.ComputedExpression;
            }
            else
            {
                expr = $"_rs_base.`{col.Name}`";
            }

            if (!string.IsNullOrEmpty(col.Alias) && col.Alias != col.Name)
            {
                expr += $" AS `{col.Alias}`";
            }

            selectParts.Add(expr);
        }

        sb.Append(string.Join(", ", selectParts));
        return sb.ToString();
    }

    internal static string BuildFromClause(string queryText)
    {
        return $" FROM ({queryText}) AS _rs_base";
    }

    internal (string Clause, List<object> Parameters) BuildWhereClause(List<FilterDefinition>? filters)
    {
        if (filters == null || filters.Count == 0)
            return ("", new List<object>());

        // Only include non-HAVING filters (those without aggregation context)
        var whereFilters = filters.Where(f =>
            !string.Equals(f.FilterType, "HAVING", StringComparison.OrdinalIgnoreCase)).ToList();

        if (whereFilters.Count == 0)
            return ("", new List<object>());

        var conditions = new List<string>();
        var allParams = new List<object>();

        foreach (var filter in whereFilters)
        {
            if (string.Equals(filter.FilterType, "PREFILTER", StringComparison.OrdinalIgnoreCase))
            {
                var (sql, parms) = _filterCompiler.CompilePrefilter(filter);
                if (!string.IsNullOrEmpty(sql))
                {
                    conditions.Add(sql);
                    allParams.AddRange(parms);
                }
            }
            else
            {
                var columnRef = $"_rs_base.`{filter.ColumnName}`";
                var (sql, parms) = _filterCompiler.CompileFilter(filter, columnRef);
                if (!string.IsNullOrEmpty(sql))
                {
                    conditions.Add(sql);
                    allParams.AddRange(parms);
                }
            }
        }

        if (conditions.Count == 0)
            return ("", new List<object>());

        return (string.Join(" AND ", conditions), allParams);
    }

    internal static string BuildGroupByClause(AggregationResult aggregation)
    {
        if (aggregation.GroupByColumns.Count == 0)
            return "";

        return " GROUP BY " + string.Join(", ", aggregation.GroupByColumns);
    }

    internal (string Clause, List<object> Parameters) BuildHavingClause(List<FilterDefinition> filters)
    {
        var havingFilters = filters.Where(f =>
            string.Equals(f.FilterType, "HAVING", StringComparison.OrdinalIgnoreCase)).ToList();

        if (havingFilters.Count == 0)
            return ("", new List<object>());

        var conditions = new List<string>();
        var allParams = new List<object>();

        foreach (var filter in havingFilters)
        {
            var columnRef = filter.ColumnName; // HAVING uses aggregated expressions directly
            var (sql, parms) = _filterCompiler.CompileFilter(filter, columnRef);
            if (!string.IsNullOrEmpty(sql))
            {
                conditions.Add(sql);
                allParams.AddRange(parms);
            }
        }

        if (conditions.Count == 0)
            return ("", new List<object>());

        return (string.Join(" AND ", conditions), allParams);
    }

    internal static string BuildOrderByClause(List<SortDefinition>? sorts)
    {
        if (sorts == null || sorts.Count == 0)
            return "";

        var parts = sorts.Select(s =>
        {
            var dir = string.Equals(s.Direction, "DESC", StringComparison.OrdinalIgnoreCase)
                ? "DESC" : "ASC";
            return $"`{s.ColumnName}` {dir}";
        });

        return " ORDER BY " + string.Join(", ", parts);
    }

    internal static string BuildLimitClause(int page, int pageSize, List<object> parameters)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 100_000) pageSize = 100_000;

        var offset = (page - 1) * pageSize;

        // Use parameter placeholders
        var limitIdx = parameters.Count;
        parameters.Add(pageSize);
        var offsetIdx = parameters.Count;
        parameters.Add(offset);

        return $" LIMIT @p{limitIdx} OFFSET @p{offsetIdx}";
    }

    // ── CTE Extraction ─────────────────────────────────────────────────────────

    internal static (string? CteClause, string BaseQuery) ExtractCte(string queryText)
    {
        var match = CteRegex.Match(queryText);
        if (!match.Success)
            return (null, queryText);

        var cteBody = match.Groups[1].Value.Trim();
        var remaining = CteRegex.Replace(queryText, "").Trim();

        // Build WITH clause
        var cteClause = $"WITH {cteBody}";
        return (cteClause, remaining);
    }
}
