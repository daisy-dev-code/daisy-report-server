namespace DaisyReport.Api.ReportEngine;

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public string? Label { get; set; }
    public int OrdinalPosition { get; set; }
}
