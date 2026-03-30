namespace DaisyReport.Api.Models;

public class GroupMember
{
    public long GroupId { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
