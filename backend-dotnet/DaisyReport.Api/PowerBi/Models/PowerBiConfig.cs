namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiConfig
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecretEncrypted { get; set; } = "";
    public string? Authority => $"https://login.microsoftonline.com/{TenantId}";
    public bool Enabled { get; set; } = true;
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
