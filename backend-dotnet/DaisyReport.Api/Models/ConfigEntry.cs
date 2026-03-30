namespace DaisyReport.Api.Models;

public class ConfigEntry
{
    public long Id { get; set; }
    public string ConfigKey { get; set; } = "";
    public string ConfigValue { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}
