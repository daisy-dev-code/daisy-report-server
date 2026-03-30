namespace DaisyReport.Api.DynamicList;

// ── Aggregation Config ─────────────────────────────────────────────────────────

public class AggregationConfig
{
    /// <summary>
    /// Key: column name/alias, Value: aggregation function
    /// (SUM, AVG, COUNT, COUNT_DISTINCT, MIN, MAX, VARIANCE)
    /// </summary>
    public Dictionary<string, string> ColumnAggregations { get; set; } = new();

    public bool IncludeSubtotals { get; set; }
}

// ── Aggregation Result (fed into SQL generator) ────────────────────────────────

public class AggregationResult
{
    /// <summary>
    /// Full SELECT expressions including aggregation functions.
    /// </summary>
    public List<string> SelectExpressions { get; set; } = new();

    /// <summary>
    /// Columns that belong in GROUP BY (non-aggregated visible columns).
    /// </summary>
    public List<string> GroupByColumns { get; set; } = new();
}

// ── Aggregation Engine ─────────────────────────────────────────────────────────

public class AggregationEngine
{
    private static readonly HashSet<string> ValidFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUM", "AVG", "COUNT", "COUNT_DISTINCT", "MIN", "MAX", "VARIANCE"
    };

    /// <summary>
    /// Produces SELECT expressions and GROUP BY columns from the column list
    /// and aggregation configuration.
    /// </summary>
    public AggregationResult Apply(List<ColumnInfo> columns, AggregationConfig config)
    {
        var result = new AggregationResult();

        foreach (var col in columns)
        {
            var colRef = $"_rs_base.`{col.Name}`";

            if (config.ColumnAggregations.TryGetValue(col.Name, out var func) ||
                config.ColumnAggregations.TryGetValue(col.Alias, out func))
            {
                if (!ValidFunctions.Contains(func))
                    throw new ArgumentException($"Invalid aggregation function: {func}");

                var expr = BuildAggregateExpression(colRef, func, col);
                result.SelectExpressions.Add($"{expr} AS `{col.Alias}`");
            }
            else
            {
                // Non-aggregated column — include in GROUP BY
                result.SelectExpressions.Add($"{colRef} AS `{col.Alias}`");
                if (!col.Hidden)
                {
                    result.GroupByColumns.Add(colRef);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Computes application-level subtotals for the given aggregation config.
    /// Adds a summary row at the end of the result set.
    /// </summary>
    public void ApplySubtotals(DynamicListResult result, AggregationConfig config)
    {
        if (!config.IncludeSubtotals || result.Rows.Count == 0)
            return;

        var subtotalRow = new Dictionary<string, object?>();

        foreach (var col in result.Columns)
        {
            if (config.ColumnAggregations.TryGetValue(col.Name, out var func) ||
                config.ColumnAggregations.TryGetValue(col.Alias, out func))
            {
                subtotalRow[col.Alias] = ComputeSubtotal(result.Rows, col.Alias, func);
            }
            else
            {
                // First group-by column gets the label
                if (subtotalRow.Count == 0)
                    subtotalRow[col.Alias] = "SUBTOTAL";
                else
                    subtotalRow[col.Alias] = null;
            }
        }

        result.Rows.Add(subtotalRow);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string BuildAggregateExpression(string colRef, string func, ColumnInfo col)
    {
        var normalizedFunc = func.ToUpperInvariant();

        return normalizedFunc switch
        {
            "COUNT_DISTINCT" => $"COUNT(DISTINCT {colRef})",
            "AVG" when IsIntegerType(col.DataType) =>
                $"AVG(CAST({colRef} AS DECIMAL(20,6)))",
            "AVG" => $"AVG({colRef})",
            "VARIANCE" => $"VARIANCE({colRef})",
            _ => $"{normalizedFunc}({colRef})"
        };
    }

    private static bool IsIntegerType(string dataType)
    {
        return dataType.ToUpperInvariant() switch
        {
            "INTEGER" or "INT" or "BIGINT" or "SMALLINT" or "TINYINT" => true,
            _ => false
        };
    }

    private static object? ComputeSubtotal(
        List<Dictionary<string, object?>> rows, string colAlias, string func)
    {
        var values = rows
            .Select(r => r.TryGetValue(colAlias, out var v) ? v : null)
            .Where(v => v != null && v is not DBNull)
            .ToList();

        if (values.Count == 0)
            return null;

        var normalizedFunc = func.ToUpperInvariant();

        try
        {
            var doubles = values
                .Select(v => Convert.ToDouble(v))
                .ToList();

            return normalizedFunc switch
            {
                "SUM" => doubles.Sum(),
                "AVG" => doubles.Average(),
                "COUNT" => (double)values.Count,
                "COUNT_DISTINCT" => (double)values.Distinct().Count(),
                "MIN" => doubles.Min(),
                "MAX" => doubles.Max(),
                "VARIANCE" => ComputeVariance(doubles),
                _ => null
            };
        }
        catch
        {
            // For non-numeric columns, only COUNT makes sense
            return normalizedFunc switch
            {
                "COUNT" => (double)values.Count,
                "COUNT_DISTINCT" => (double)values.Distinct().Count(),
                _ => null
            };
        }
    }

    private static double ComputeVariance(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / values.Count;
    }
}
