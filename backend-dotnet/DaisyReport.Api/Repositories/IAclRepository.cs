namespace DaisyReport.Api.Repositories;

public interface IAclRepository
{
    Task<List<string>> GetRolePermissionsAsync(string role);
    Task<List<string>> GetUserPermissionsAsync(long userId);
    Task GrantUserPermissionAsync(long userId, string permission);
    Task RevokeUserPermissionAsync(long userId, string permission);
}
