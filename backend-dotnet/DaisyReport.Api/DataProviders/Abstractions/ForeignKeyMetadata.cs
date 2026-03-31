namespace DaisyReport.Api.DataProviders.Abstractions;

public sealed class ForeignKeyMetadata
{
    public string ConstraintName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public string ReferencedColumn { get; set; } = string.Empty;
    public string? ReferencedSchema { get; set; }
    public string? TableName { get; set; }
    public string? Schema { get; set; }
}
