using Dapper;
using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.Repositories;

public class AclRepository : IAclRepository
{
    private readonly IDatabase _database;

    public AclRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<List<string>> GetRolePermissionsAsync(string role)
    {
        using var conn = await _database.GetConnectionAsync();
        var permissions = await conn.QueryAsync<string>(
            @"SELECT permission FROM RS_ROLE_PERMISSIONS WHERE role = @Role",
            new { Role = role });
        return permissions.ToList();
    }

    public async Task<List<string>> GetUserPermissionsAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var permissions = await conn.QueryAsync<string>(
            @"SELECT permission FROM RS_USER_PERMISSIONS WHERE user_id = @UserId",
            new { UserId = userId });
        return permissions.ToList();
    }

    public async Task GrantUserPermissionAsync(long userId, string permission)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT IGNORE INTO RS_USER_PERMISSIONS (user_id, permission, created_at)
              VALUES (@UserId, @Permission, @Now)",
            new { UserId = userId, Permission = permission, Now = DateTime.UtcNow });
    }

    public async Task RevokeUserPermissionAsync(long userId, string permission)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"DELETE FROM RS_USER_PERMISSIONS WHERE user_id = @UserId AND permission = @Permission",
            new { UserId = userId, Permission = permission });
    }
}
