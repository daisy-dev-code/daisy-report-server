using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DaisyReport.Api.ReportEngine;

public interface IOutputFormatter
{
    Task<ReportExecutionResult> FormatAsync(ReportExecutionResult rawResult, string format);
}

public class OutputFormatter : IOutputFormatter
{
    public Task<ReportExecutionResult> FormatAsync(ReportExecutionResult rawResult, string format)
    {
        if (!rawResult.Success || rawResult.Rows == null)
            return Task.FromResult(rawResult);

        return format.ToUpperInvariant() switch
        {
            "HTML" => Task.FromResult(FormatHtml(rawResult)),
            "JSON" => Task.FromResult(FormatJson(rawResult)),
            "CSV" => Task.FromResult(FormatCsv(rawResult)),
            "EXCEL" => Task.FromResult(FormatExcel(rawResult)),
            "PDF" => Task.FromResult(FormatPdf(rawResult)),
            _ => throw new ArgumentException($"Unsupported output format: {format}")
        };
    }

    private static ReportExecutionResult FormatHtml(ReportExecutionResult result)
    {
        var sb = new StringBuilder();
        var columns = result.Columns ?? [];
        var rows = result.Rows ?? [];

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><style>");
        sb.AppendLine("table.report { border-collapse: collapse; width: 100%; font-family: sans-serif; font-size: 14px; }");
        sb.AppendLine("table.report th { background-color: #2563eb; color: white; padding: 8px 12px; text-align: left; border: 1px solid #1d4ed8; }");
        sb.AppendLine("table.report td { padding: 6px 12px; border: 1px solid #e5e7eb; }");
        sb.AppendLine("table.report tr:nth-child(even) { background-color: #f9fafb; }");
        sb.AppendLine("table.report tr:hover { background-color: #eff6ff; }");
        sb.AppendLine(".report-footer { margin-top: 8px; font-size: 12px; color: #6b7280; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<table class=\"report\">");

        // Header row
        sb.AppendLine("<thead><tr>");
        foreach (var col in columns)
        {
            sb.AppendLine($"  <th>{EscapeHtml(col.Label ?? col.Name)}</th>");
        }
        sb.AppendLine("</tr></thead>");

        // Data rows
        sb.AppendLine("<tbody>");
        foreach (var row in rows)
        {
            sb.AppendLine("<tr>");
            foreach (var col in columns)
            {
                var cellValue = row.TryGetValue(col.Name, out var val) ? val?.ToString() ?? "" : "";
                sb.AppendLine($"  <td>{EscapeHtml(cellValue)}</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        // Footer
        sb.AppendLine($"<div class=\"report-footer\">{rows.Count} row(s) returned");
        if (result.TotalRows.HasValue)
        {
            sb.AppendLine($" of {result.TotalRows.Value} total");
        }
        sb.AppendLine($" | {result.ExecutionTimeMs}ms</div>");

        sb.AppendLine("</body></html>");

        result.DataAsString = sb.ToString();
        result.Data = Encoding.UTF8.GetBytes(result.DataAsString);
        result.ContentType = "text/html";
        return result;
    }

    private static ReportExecutionResult FormatJson(ReportExecutionResult result)
    {
        var payload = new
        {
            columns = result.Columns?.Select(c => new
            {
                name = c.Name,
                label = c.Label ?? c.Name,
                dataType = c.DataType,
                ordinalPosition = c.OrdinalPosition
            }),
            rows = result.Rows,
            pagination = new
            {
                totalRows = result.TotalRows,
                executionTimeMs = result.ExecutionTimeMs
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        result.DataAsString = json;
        result.Data = Encoding.UTF8.GetBytes(json);
        result.ContentType = "application/json";
        return result;
    }

    private static ReportExecutionResult FormatCsv(ReportExecutionResult result, string delimiter = ",")
    {
        var sb = new StringBuilder();
        var columns = result.Columns ?? [];
        var rows = result.Rows ?? [];

        // Header
        sb.AppendLine(string.Join(delimiter, columns.Select(c => CsvEscape(c.Label ?? c.Name, delimiter))));

        // Data rows
        foreach (var row in rows)
        {
            var values = columns.Select(col =>
            {
                var val = row.TryGetValue(col.Name, out var v) ? v?.ToString() ?? "" : "";
                return CsvEscape(val, delimiter);
            });
            sb.AppendLine(string.Join(delimiter, values));
        }

        result.DataAsString = sb.ToString();
        result.Data = Encoding.UTF8.GetBytes(result.DataAsString);
        result.ContentType = "text/csv";
        return result;
    }

    private static ReportExecutionResult FormatExcel(ReportExecutionResult result)
    {
        // Stub: returns CSV with Excel content type
        // A full implementation would use a library like ClosedXML
        var csvResult = FormatCsv(result);
        csvResult.ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return csvResult;
    }

    private static ReportExecutionResult FormatPdf(ReportExecutionResult result)
    {
        // Stub: returns HTML with PDF content type
        // A full implementation would use a library like QuestPDF or wkhtmltopdf
        var htmlResult = FormatHtml(result);
        htmlResult.ContentType = "application/pdf";
        return htmlResult;
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    /// <summary>
    /// RFC 4180 CSV escaping: wrap in quotes if field contains delimiter, quote, or newline.
    /// </summary>
    private static string CsvEscape(string field, string delimiter)
    {
        if (field.Contains(delimiter, StringComparison.Ordinal) ||
            field.Contains('"') ||
            field.Contains('\n') ||
            field.Contains('\r'))
        {
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        }
        return field;
    }
}
