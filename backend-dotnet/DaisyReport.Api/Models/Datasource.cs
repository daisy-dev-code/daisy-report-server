namespace DaisyReport.Api.Models;

public class Datasource
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Dtype { get; set; } = string.Empty;
    public long? FolderId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // DatabaseDatasource sub-properties
    public string? DriverClass { get; set; }
    public string? JdbcUrl { get; set; }
    public string? Username { get; set; }
    public string? PasswordEncrypted { get; set; }
    public int? MinPool { get; set; }
    public int? MaxPool { get; set; }
    public int? QueryTimeout { get; set; }
}
