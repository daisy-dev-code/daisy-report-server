namespace DaisyReport.Api.DataProviders.Abstractions;

public sealed class TableMetadata
{
    public string Name { get; set; } = string.Empty;
    public string? Schema { get; set; }
    public string TableType { get; set; } = "TABLE"; // TABLE or VIEW
}
