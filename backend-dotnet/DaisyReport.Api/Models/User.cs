namespace DaisyReport.Api.Models;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Firstname { get; set; }
    public string? Lastname { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? LockedUntil { get; set; }
    public int LoginFailures { get; set; }
    public DateTime? PasswordChanged { get; set; }
    public string? OtpHash { get; set; }
    public DateTime? OtpExpires { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Computed properties for API compatibility
    public string DisplayName => $"{Firstname ?? ""} {Lastname ?? ""}".Trim();
    public bool IsActive => Enabled && (LockedUntil == null || LockedUntil < DateTime.UtcNow);
}
