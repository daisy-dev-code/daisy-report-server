namespace DaisyReport.Api.PowerBi.Models;

public class PowerBiWorkspace
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? State { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsOnDedicatedCapacity { get; set; }
}
