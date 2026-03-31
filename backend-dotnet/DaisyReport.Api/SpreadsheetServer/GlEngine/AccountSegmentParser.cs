using System.Text.RegularExpressions;

namespace DaisyReport.Api.SpreadsheetServer.GlEngine;

/// <summary>
/// Parses segmented GL account numbers based on a configurable segment format.
/// Supports delimiters: -, ., /, _ and fixed-width (no delimiter) segments.
/// </summary>
public class AccountSegmentParser
{
    private static readonly char[] Delimiters = { '-', '.', '/', '_' };

    /// <summary>
    /// Parse an account string using the given segment format.
    /// Format example: "{Company}-{Account}-{Department}"
    /// Returns: { "Company":"01", "Account":"110", "Department":"1000" }
    /// </summary>
    public Dictionary<string, string> Parse(string accountString, string segmentFormat)
    {
        if (string.IsNullOrWhiteSpace(accountString))
            return new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(segmentFormat))
            return new Dictionary<string, string> { ["Account"] = accountString };

        // Extract segment names from format: {Name1}-{Name2}-{Name3}
        var segmentNames = ExtractSegmentNames(segmentFormat);
        if (segmentNames.Count == 0)
            return new Dictionary<string, string> { ["Account"] = accountString };

        // Detect delimiter used in the format
        char? delimiter = DetectDelimiter(segmentFormat);

        if (delimiter.HasValue)
        {
            return ParseDelimited(accountString, segmentNames, delimiter.Value);
        }

        // No delimiter detected -- use fixed-width parsing based on format token lengths
        return ParseFixedWidth(accountString, segmentFormat, segmentNames);
    }

    /// <summary>
    /// Build a SQL WHERE clause from parsed segments and a column mapping.
    /// columnMapping maps segment name to actual DB column name.
    /// Example: { "Company":"co_code", "Account":"acct_no" }
    /// </summary>
    public string BuildWhereClause(Dictionary<string, string> segments, Dictionary<string, string> columnMapping)
    {
        var clauses = new List<string>();

        foreach (var (segmentName, segmentValue) in segments)
        {
            if (columnMapping.TryGetValue(segmentName, out var columnName))
            {
                // Use parameterized placeholders -- caller must bind @seg_{segmentName}
                clauses.Add($"{columnName} = @seg_{segmentName}");
            }
        }

        return clauses.Count > 0
            ? string.Join(" AND ", clauses)
            : "1=1";
    }

    /// <summary>
    /// Build a Dapper DynamicParameters dict for the segment values.
    /// </summary>
    public Dictionary<string, object> BuildSegmentParameters(Dictionary<string, string> segments)
    {
        var parameters = new Dictionary<string, object>();
        foreach (var (name, value) in segments)
        {
            parameters[$"seg_{name}"] = value;
        }
        return parameters;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<string> ExtractSegmentNames(string format)
    {
        var matches = Regex.Matches(format, @"\{(\w+)\}");
        var names = new List<string>();
        foreach (Match match in matches)
        {
            names.Add(match.Groups[1].Value);
        }
        return names;
    }

    private static char? DetectDelimiter(string format)
    {
        // Check which delimiter appears in the format string outside of { } blocks
        var stripped = Regex.Replace(format, @"\{[^}]+\}", "");
        foreach (var d in Delimiters)
        {
            if (stripped.Contains(d))
                return d;
        }
        return null;
    }

    private static Dictionary<string, string> ParseDelimited(
        string accountString, List<string> segmentNames, char delimiter)
    {
        var parts = accountString.Split(delimiter);
        var result = new Dictionary<string, string>();

        for (int i = 0; i < segmentNames.Count && i < parts.Length; i++)
        {
            result[segmentNames[i]] = parts[i].Trim();
        }

        return result;
    }

    private static Dictionary<string, string> ParseFixedWidth(
        string accountString, string format, List<string> segmentNames)
    {
        // Extract widths from format by measuring the length of each {Name} token position
        // E.g., "{CC:2}{Acct:4}{Dept:3}" or simply "{CC}{Acct}{Dept}" with widths from separating chars
        var widthPattern = Regex.Matches(format, @"\{(\w+)(?::(\d+))?\}");
        var result = new Dictionary<string, string>();
        int position = 0;

        foreach (Match match in widthPattern)
        {
            var name = match.Groups[1].Value;

            int width;
            if (match.Groups[2].Success)
            {
                // Explicit width: {Name:4}
                width = int.Parse(match.Groups[2].Value);
            }
            else
            {
                // No explicit width -- for the last segment take remaining, otherwise
                // split evenly as a fallback
                if (match.NextMatch().Success)
                {
                    // Try to calculate based on total segments
                    width = accountString.Length / segmentNames.Count;
                }
                else
                {
                    width = accountString.Length - position;
                }
            }

            if (position + width > accountString.Length)
                width = accountString.Length - position;

            if (width > 0 && position < accountString.Length)
            {
                result[name] = accountString.Substring(position, width).Trim();
                position += width;
            }
        }

        return result;
    }
}
