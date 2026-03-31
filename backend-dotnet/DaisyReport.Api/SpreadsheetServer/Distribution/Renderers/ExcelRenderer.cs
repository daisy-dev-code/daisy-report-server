using ClosedXML.Excel;

namespace DaisyReport.Api.SpreadsheetServer.Distribution.Renderers;

public class ExcelRenderer
{
    private readonly FormulaResolver _resolver;
    private readonly ILogger<ExcelRenderer> _logger;

    public ExcelRenderer(FormulaResolver resolver, ILogger<ExcelRenderer> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Loads an Excel template, resolves all DaisySheet formulas (=DS* and =GXL*),
    /// and returns the modified workbook as a byte array.
    /// </summary>
    public async Task<byte[]> RenderAsync(byte[] templateBytes, long defaultConnectionId,
        Dictionary<string, string>? burstParameters = null, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(templateBytes);
        using var workbook = new XLWorkbook(stream);

        foreach (var worksheet in workbook.Worksheets)
        {
            var usedRange = worksheet.RangeUsed();
            if (usedRange == null) continue;

            foreach (var cell in usedRange.CellsUsed())
            {
                ct.ThrowIfCancellationRequested();

                // Check if cell has a formula that starts with DS or GXL
                string? formula = null;

                if (cell.HasFormula)
                {
                    formula = cell.FormulaA1;
                }
                else if (cell.Value.IsText)
                {
                    // Some templates store formulas as text prefixed with =
                    var text = cell.GetText();
                    if (text.StartsWith("=DS", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("=GXL", StringComparison.OrdinalIgnoreCase))
                    {
                        formula = text;
                    }
                }

                if (string.IsNullOrEmpty(formula)) continue;

                // Only process DaisySheet formulas
                var trimmed = formula.TrimStart('=');
                if (!trimmed.StartsWith("DS", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("GXL", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    // Apply burst parameters by replacing placeholders in the formula
                    var resolvedFormula = ApplyBurstParameters(formula, burstParameters);

                    var result = await _resolver.ResolveAsync(resolvedFormula, defaultConnectionId);

                    if (result == null)
                    {
                        cell.Clear();
                    }
                    else
                    {
                        SetCellValue(cell, result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve formula in {Sheet}!{Cell}: {Formula}",
                        worksheet.Name, cell.Address, formula);
                    cell.Value = $"#ERROR: {ex.Message}";
                }
            }
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>
    /// Returns the resolved workbook as an XLWorkbook for further processing (CSV/HTML).
    /// </summary>
    public async Task<XLWorkbook> RenderToWorkbookAsync(byte[] templateBytes, long defaultConnectionId,
        Dictionary<string, string>? burstParameters = null, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(templateBytes);
        var workbook = new XLWorkbook(stream);

        foreach (var worksheet in workbook.Worksheets)
        {
            var usedRange = worksheet.RangeUsed();
            if (usedRange == null) continue;

            foreach (var cell in usedRange.CellsUsed())
            {
                ct.ThrowIfCancellationRequested();

                string? formula = null;

                if (cell.HasFormula)
                {
                    formula = cell.FormulaA1;
                }
                else if (cell.Value.IsText)
                {
                    var text = cell.GetText();
                    if (text.StartsWith("=DS", StringComparison.OrdinalIgnoreCase) ||
                        text.StartsWith("=GXL", StringComparison.OrdinalIgnoreCase))
                    {
                        formula = text;
                    }
                }

                if (string.IsNullOrEmpty(formula)) continue;

                var trimmed = formula.TrimStart('=');
                if (!trimmed.StartsWith("DS", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("GXL", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var resolvedFormula = ApplyBurstParameters(formula, burstParameters);
                    var result = await _resolver.ResolveAsync(resolvedFormula, defaultConnectionId);

                    if (result == null)
                        cell.Clear();
                    else
                        SetCellValue(cell, result);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve formula in {Sheet}!{Cell}: {Formula}",
                        worksheet.Name, cell.Address, formula);
                    cell.Value = $"#ERROR: {ex.Message}";
                }
            }
        }

        return workbook;
    }

    private static string ApplyBurstParameters(string formula, Dictionary<string, string>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return formula;

        var result = formula;
        foreach (var (key, value) in parameters)
        {
            // Replace {{param}} placeholders
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.OrdinalIgnoreCase);
            // Also replace {param} style
            result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static void SetCellValue(IXLCell cell, object value)
    {
        // Clear any existing formula
        cell.Clear();

        switch (value)
        {
            case int i:
                cell.Value = i;
                break;
            case long l:
                cell.Value = l;
                break;
            case decimal d:
                cell.Value = (double)d;
                break;
            case double dbl:
                cell.Value = dbl;
                break;
            case float f:
                cell.Value = (double)f;
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case bool b:
                cell.Value = b;
                break;
            case string s:
                cell.Value = s;
                break;
            default:
                cell.Value = value.ToString() ?? "";
                break;
        }
    }
}
