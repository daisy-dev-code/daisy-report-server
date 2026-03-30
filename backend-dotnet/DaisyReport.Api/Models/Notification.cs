namespace DaisyReport.Api.Models;

public class Notification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Message { get; set; }
    public bool ReadFlag { get; set; }
    public string? Link { get; set; }
    public DateTime CreatedAt { get; set; }
}
