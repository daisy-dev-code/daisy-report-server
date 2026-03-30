using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DaisyReport.Api.DynamicList;

// ── Contracts ──────────────────────────────────────────────────────────────────

public interface IExportService
{
    byte[] ExportCsv(DynamicListResult data, CsvExportOptions? options = null);
    byte[] ExportJson(DynamicListResult data);
    byte[] ExportHtml(DynamicListResult data, string? title = null);
}

public class CsvExportOptions
{
    public string Separator { get; set; } = ",";
    public string QuoteChar { get; set; } = "\"";
    public string Encoding { get; set; } = "UTF-8";
    public bool IncludeHeader { get; set; } = true;
}

// ── Export Service ─────────────────────────────────────────────────────────────

public class ExportService : IExportService
{
    /// <summary>
    /// Exports to CSV following RFC 4180: CRLF line endings, quote fields
    /// containing the separator, newlines, or quote characters.
    /// </summary>
    public byte[] ExportCsv(DynamicListResult data, CsvExportOptions? options = null)
    {
        options ??= new CsvExportOptions();
        var encoding = GetEncoding(options.Encoding);
        var sep = options.Separator;
        var quote = options.QuoteChar;

        var sb = new StringBuilder();

        // Visible columns only
        var visibleColumns = data.Columns.Where(c => !c.Hidden).ToList();

        // Header row
        if (options.IncludeHeader)
        {
            sb.Append(string.Join(sep, visibleColumns.Select(c => CsvEscape(c.Alias, sep, quote))));
            sb.Append("\r\n");
        }

        // Data rows
        foreach (var row in data.Rows)
        {
            var fields = visibleColumns.Select(col =>
            {
                var value = row.TryGetValue(col.Alias, out var v) ? v : null;
                var str = FormatValue(value, col.Format);
                return CsvEscape(str, sep, quote);
            });

            sb.Append(string.Join(sep, fields));
            sb.Append("\r\n");
        }

        return encoding.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Exports as a JSON array of objects.
    /// </summary>
    public byte[] ExportJson(DynamicListResult data)
    {
        var visibleColumns = data.Columns.Where(c => !c.Hidden).ToList();
        var columnNames = visibleColumns.Select(c => c.Alias).ToHashSet();

        // Build filtered row list containing only visible columns
        var exportRows = data.Rows.Select(row =>
        {
            var filtered = new Dictionary<string, object?>();
            foreach (var col in visibleColumns)
            {
                filtered[col.Alias] = row.TryGetValue(col.Alias, out var v) ? v : null;
            }
            return filtered;
        }).ToList();

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(exportRows, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return jsonBytes;
    }

    /// <summary>
    /// Exports as an HTML table with CSS classes for styling.
    /// </summary>
    public byte[] ExportHtml(DynamicListResult data, string? title = null)
    {
        var visibleColumns = data.Columns.Where(c => !c.Hidden).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>{HtmlEncode(title ?? "Report")}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(@"
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 20px; }
            .rs-table { border-collapse: collapse; width: 100%; font-size: 14px; }
            .rs-table th { background-color: #f8f9fa; border: 1px solid #dee2e6; padding: 8px 12px; text-align: left; font-weight: 600; }
            .rs-table td { border: 1px solid #dee2e6; padding: 6px 12px; }
            .rs-table tr:nth-child(even) { background-color: #f8f9fa; }
            .rs-table tr:hover { background-color: #e9ecef; }
            .rs-numeric { text-align: right; font-variant-numeric: tabular-nums; }
            .rs-header { margin-bottom: 16px; }
            .rs-footer { margin-top: 12px; font-size: 12px; color: #6c757d; }
        ");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        if (!string.IsNullOrEmpty(title))
        {
            sb.AppendLine($"<div class=\"rs-header\"><h2>{HtmlEncode(title)}</h2></div>");
        }

        sb.AppendLine("<table class=\"rs-table\">");

        // Header
        sb.AppendLine("<thead><tr>");
        foreach (var col in visibleColumns)
        {
            var widthAttr = col.Width.HasValue ? $" style=\"width:{col.Width}px\"" : "";
            sb.AppendLine($"  <th{widthAttr}>{HtmlEncode(col.Alias)}</th>");
        }
        sb.AppendLine("</tr></thead>");

        // Body
        sb.AppendLine("<tbody>");
        foreach (var row in data.Rows)
        {
            sb.AppendLine("<tr>");
            foreach (var col in visibleColumns)
            {
                var value = row.TryGetValue(col.Alias, out var v) ? v : null;
                var str = FormatValue(value, col.Format);
                var cssClass = IsNumericType(col.DataType) ? " class=\"rs-numeric\"" : "";
                sb.AppendLine($"  <td{cssClass}>{HtmlEncode(str)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody>");

        sb.AppendLine("</table>");

        sb.AppendLine($"<div class=\"rs-footer\">{data.TotalRows:N0} row(s) &middot; Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</div>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string CsvEscape(string value, string separator, string quote)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Must quote if value contains separator, quote char, or newline
        bool needsQuoting = value.Contains(separator) ||
                            value.Contains(quote) ||
                            value.Contains('\n') ||
                            value.Contains('\r');

        if (!needsQuoting)
            return value;

        // Double any existing quote chars
        var escaped = value.Replace(quote, quote + quote);
        return $"{quote}{escaped}{quote}";
    }

    private static string FormatValue(object? value, string? format)
    {
        if (value == null || value is DBNull)
            return "";

        if (!string.IsNullOrEmpty(format))
        {
            try
            {
                if (value is IFormattable formattable)
                    return formattable.ToString(format, CultureInfo.InvariantCulture);
            }
            catch
            {
                // Fall through to default
            }
        }

        return value.ToString() ?? "";
    }

    private static string HtmlEncode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value ?? "");
    }

    private static bool IsNumericType(string dataType)
    {
        return dataType.ToUpperInvariant() switch
        {
            "INTEGER" or "INT" or "DECIMAL" or "FLOAT" or "DOUBLE" or "NUMERIC" => true,
            _ => false
        };
    }

    private static Encoding GetEncoding(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "UTF-8" or "UTF8" => new UTF8Encoding(false),
            "UTF-16" or "UTF16" or "UNICODE" => Encoding.Unicode,
            "ASCII" => Encoding.ASCII,
            "LATIN1" or "ISO-8859-1" => Encoding.Latin1,
            _ => new UTF8Encoding(false)
        };
    }
}
