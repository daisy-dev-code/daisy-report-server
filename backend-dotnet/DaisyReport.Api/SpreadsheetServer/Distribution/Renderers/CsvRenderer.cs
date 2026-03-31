using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace DaisyReport.Api.SpreadsheetServer.Distribution.Renderers;

public class CsvRenderer
{
    /// <summary>
    /// Converts the first worksheet of an XLWorkbook to CSV bytes.
    /// </summary>
    public byte[] Render(XLWorkbook workbook, string separator = ",")
    {
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null)
            return Encoding.UTF8.GetBytes("");

        var usedRange = worksheet.RangeUsed();
        if (usedRange == null)
            return Encoding.UTF8.GetBytes("");

        var sb = new StringBuilder();
        var firstRow = usedRange.FirstRow().RowNumber();
        var lastRow = usedRange.LastRow().RowNumber();
        var firstCol = usedRange.FirstColumn().ColumnNumber();
        var lastCol = usedRange.LastColumn().ColumnNumber();

        for (int row = firstRow; row <= lastRow; row++)
        {
            var fields = new List<string>();
            for (int col = firstCol; col <= lastCol; col++)
            {
                var cell = worksheet.Cell(row, col);
                var value = FormatCellValue(cell);
                fields.Add(CsvEscape(value, separator));
            }
            sb.AppendLine(string.Join(separator, fields));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string FormatCellValue(IXLCell cell)
    {
        if (cell.IsEmpty())
            return "";

        if (cell.Value.IsNumber)
            return cell.Value.GetNumber().ToString(CultureInfo.InvariantCulture);

        if (cell.Value.IsDateTime)
            return cell.Value.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        if (cell.Value.IsBoolean)
            return cell.Value.GetBoolean() ? "TRUE" : "FALSE";

        if (cell.Value.IsText)
            return cell.GetText();

        return cell.Value.ToString() ?? "";
    }

    private static string CsvEscape(string value, string separator)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        bool needsQuoting = value.Contains(separator) ||
                            value.Contains('"') ||
                            value.Contains('\n') ||
                            value.Contains('\r');

        if (!needsQuoting)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
