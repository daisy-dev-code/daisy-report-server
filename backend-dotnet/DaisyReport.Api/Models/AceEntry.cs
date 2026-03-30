namespace DaisyReport.Api.Models;

public class AceEntry
{
    public long Id { get; set; }
    public long AclId { get; set; }
    public string PrincipalType { get; set; } = "";
    public long PrincipalId { get; set; }
    public string AccessType { get; set; } = ""; // GRANT or REVOKE
    public string Permission { get; set; } = "";
    public bool Inherit { get; set; } = true;
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; }
}
