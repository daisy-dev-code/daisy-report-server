namespace DaisyReport.Api.Models;

public class ReportParameter
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? KeyField { get; set; }
    public string Type { get; set; } = "text";
    public string? DefaultValue { get; set; }
    public bool Mandatory { get; set; }
    public int Position { get; set; }
}
