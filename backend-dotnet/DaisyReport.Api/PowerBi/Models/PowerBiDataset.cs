namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiDataset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? WebUrl { get; set; }
    public bool IsRefreshable { get; set; }
    public bool IsEffectiveIdentityRequired { get; set; }
    public string? ConfiguredBy { get; set; }
    public DateTime? CreatedDate { get; set; }
}
