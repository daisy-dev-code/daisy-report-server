namespace DaisyReport.Api.DynamicList;

// ── Pivot Config ───────────────────────────────────────────────────────────────

public class PivotConfig
{
    public string RowColumn { get; set; } = "";
    public string PivotColumn { get; set; } = "";
    public string ValueColumn { get; set; } = "";
    public string Aggregation { get; set; } = "SUM";
}

// ── Pivot Transformer ──────────────────────────────────────────────────────────

public class PivotTransformer
{
    /// <summary>
    /// Transforms a flat result set into a crosstab/pivot table.
    /// Rows are grouped by RowColumn, columns are created from distinct PivotColumn values,
    /// and cells contain aggregated ValueColumn values.
    /// </summary>
    public DynamicListResult Transform(DynamicListResult input, PivotConfig config)
    {
        if (input.Rows.Count == 0)
            return input;

        ValidateConfig(config, input);

        // Collect distinct pivot values (these become column headers)
        var pivotValues = input.Rows
            .Select(r => GetValue(r, config.PivotColumn)?.ToString() ?? "(null)")
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        // Group rows by the row column
        var groups = input.Rows
            .GroupBy(r => GetValue(r, config.RowColumn)?.ToString() ?? "(null)")
            .OrderBy(g => g.Key);

        // Build pivoted rows
        var pivotedRows = new List<Dictionary<string, object?>>();
        foreach (var group in groups)
        {
            var row = new Dictionary<string, object?>
            {
                [config.RowColumn] = group.Key
            };

            foreach (var pivotVal in pivotValues)
            {
                var matchingRows = group
                    .Where(r => (GetValue(r, config.PivotColumn)?.ToString() ?? "(null)") == pivotVal)
                    .ToList();

                row[pivotVal] = Aggregate(matchingRows, config.ValueColumn, config.Aggregation);
            }

            pivotedRows.Add(row);
        }

        // Build column info
        var columns = new List<ColumnInfo>
        {
            new()
            {
                Name = config.RowColumn,
                Alias = config.RowColumn,
                DataType = "STRING"
            }
        };

        foreach (var pv in pivotValues)
        {
            columns.Add(new ColumnInfo
            {
                Name = pv,
                Alias = pv,
                DataType = "DECIMAL"
            });
        }

        return new DynamicListResult
        {
            Columns = columns,
            Rows = pivotedRows,
            TotalRows = pivotedRows.Count,
            Page = 1,
            PageSize = pivotedRows.Count,
            GeneratedSql = input.GeneratedSql
        };
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static void ValidateConfig(PivotConfig config, DynamicListResult input)
    {
        if (string.IsNullOrWhiteSpace(config.RowColumn))
            throw new ArgumentException("Pivot RowColumn is required.");
        if (string.IsNullOrWhiteSpace(config.PivotColumn))
            throw new ArgumentException("Pivot PivotColumn is required.");
        if (string.IsNullOrWhiteSpace(config.ValueColumn))
            throw new ArgumentException("Pivot ValueColumn is required.");

        var knownColumns = input.Rows.Count > 0
            ? input.Rows[0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

        if (knownColumns.Count > 0)
        {
            if (!knownColumns.Contains(config.RowColumn))
                throw new ArgumentException($"Pivot RowColumn '{config.RowColumn}' not found in result.");
            if (!knownColumns.Contains(config.PivotColumn))
                throw new ArgumentException($"Pivot PivotColumn '{config.PivotColumn}' not found in result.");
            if (!knownColumns.Contains(config.ValueColumn))
                throw new ArgumentException($"Pivot ValueColumn '{config.ValueColumn}' not found in result.");
        }
    }

    private static object? GetValue(Dictionary<string, object?> row, string column)
    {
        // Case-insensitive lookup
        foreach (var kv in row)
        {
            if (string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    private static object? Aggregate(
        List<Dictionary<string, object?>> rows, string valueColumn, string aggregation)
    {
        var values = rows
            .Select(r => GetValue(r, valueColumn))
            .Where(v => v != null && v is not DBNull)
            .ToList();

        if (values.Count == 0)
            return null;

        try
        {
            var doubles = values.Select(v => Convert.ToDouble(v)).ToList();

            return aggregation.ToUpperInvariant() switch
            {
                "SUM" => doubles.Sum(),
                "AVG" => doubles.Average(),
                "COUNT" => (double)values.Count,
                "COUNT_DISTINCT" => (double)values.Distinct().Count(),
                "MIN" => doubles.Min(),
                "MAX" => doubles.Max(),
                _ => doubles.Sum()
            };
        }
        catch
        {
            return aggregation.ToUpperInvariant() switch
            {
                "COUNT" => (double)values.Count,
                "COUNT_DISTINCT" => (double)values.Distinct().Count(),
                _ => null
            };
        }
    }
}
