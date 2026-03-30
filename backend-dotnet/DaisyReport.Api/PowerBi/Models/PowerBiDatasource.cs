namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiDatasource
{
    public string DatasourceId { get; set; } = "";
    public string? GatewayId { get; set; }
    public string DatasourceType { get; set; } = "";
    public PowerBiConnectionDetails? ConnectionDetails { get; set; }
}

public class PowerBiConnectionDetails
{
    public string? Server { get; set; }
    public string? Database { get; set; }
    public string? Url { get; set; }
    public string? Path { get; set; }
}
