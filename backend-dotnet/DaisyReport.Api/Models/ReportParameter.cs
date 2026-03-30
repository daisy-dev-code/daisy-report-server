namespace DaisyReport.Api.Models;

public class ReportParameter
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string ParamType { get; set; } = "text";
    public string? DefaultValue { get; set; }
    public bool Required { get; set; }
    public int SortOrder { get; set; }
}
