namespace DaisyReport.Api.Services;

public interface IAclService
{
    Task<bool> HasPermissionAsync(long userId, string permission);
    Task<List<string>> GetPermissionsAsync(long userId);
    Task GrantPermissionAsync(long userId, string permission);
    Task RevokePermissionAsync(long userId, string permission);
    Task<List<string>> GetRolePermissionsAsync(string role);
}
