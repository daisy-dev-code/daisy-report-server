using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class AclRepository : IAclRepository
{
    private readonly IDatabase _database;

    public AclRepository(IDatabase database)
    {
        _database = database;
    }

    // ─── Legacy role/permission operations ───────────────────────────────

    public Task<List<string>> GetRolePermissionsAsync(string role)
    {
        // RS_ROLE_PERMISSIONS table does not exist in current schema - stub returns empty list
        return Task.FromResult(new List<string>());
    }

    public Task<List<string>> GetUserPermissionsAsync(long userId)
    {
        // RS_USER_PERMISSIONS table does not exist in current schema - stub returns empty list
        return Task.FromResult(new List<string>());
    }

    public Task GrantUserPermissionAsync(long userId, string permission)
    {
        // RS_USER_PERMISSIONS table does not exist in current schema - no-op
        return Task.CompletedTask;
    }

    public Task RevokeUserPermissionAsync(long userId, string permission)
    {
        // RS_USER_PERMISSIONS table does not exist in current schema - no-op
        return Task.CompletedTask;
    }

    // ─── ACL operations ──────────────────────────────────────────────────

    public async Task<long?> GetAclIdAsync(string entityType, long entityId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT id FROM RS_ACL WHERE entity_type = @EntityType AND entity_id = @EntityId",
            new { EntityType = entityType, EntityId = entityId });
    }

    public async Task<long> CreateAclAsync(string entityType, long entityId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_ACL (entity_type, entity_id, inherit, created_at)
              VALUES (@EntityType, @EntityId, 1, @Now)",
            new { EntityType = entityType, EntityId = entityId, Now = DateTime.UtcNow });
        return await conn.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()");
    }

    public async Task<List<AceEntry>> GetAcesByAclIdAsync(long aclId)
    {
        using var conn = await _database.GetConnectionAsync();
        var results = await conn.QueryAsync<AceEntry>(
            @"SELECT id AS Id, acl_id AS AclId, principal_type AS PrincipalType,
                     principal_id AS PrincipalId, access_type AS AccessType,
                     permission AS Permission, inherit AS Inherit,
                     position AS Position, created_at AS CreatedAt
              FROM RS_ACE
              WHERE acl_id = @AclId
              ORDER BY position ASC",
            new { AclId = aclId });
        return results.ToList();
    }

    public async Task<bool> GetAclInheritFlagAsync(long aclId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<bool>(
            "SELECT inherit FROM RS_ACL WHERE id = @AclId",
            new { AclId = aclId });
    }

    // ─── ACE operations ──────────────────────────────────────────────────

    public async Task<long> AddAceAsync(long aclId, string principalType, long principalId,
        string accessType, string permission, bool inherit, int position)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_ACE (acl_id, principal_type, principal_id, access_type,
                                  permission, inherit, position, created_at)
              VALUES (@AclId, @PrincipalType, @PrincipalId, @AccessType,
                      @Permission, @Inherit, @Position, @Now)",
            new
            {
                AclId = aclId,
                PrincipalType = principalType,
                PrincipalId = principalId,
                AccessType = accessType,
                Permission = permission,
                Inherit = inherit,
                Position = position,
                Now = DateTime.UtcNow
            });
        return await conn.QuerySingleAsync<long>("SELECT LAST_INSERT_ID()");
    }

    public async Task<bool> RemoveAceAsync(long aceId)
    {
        using var conn = await _database.GetConnectionAsync();
        var affected = await conn.ExecuteAsync(
            "DELETE FROM RS_ACE WHERE id = @AceId",
            new { AceId = aceId });
        return affected > 0;
    }

    public async Task<List<AceEntry>> GetAcesForEntityAsync(string entityType, long entityId)
    {
        using var conn = await _database.GetConnectionAsync();
        var results = await conn.QueryAsync<AceEntry>(
            @"SELECT e.id AS Id, e.acl_id AS AclId, e.principal_type AS PrincipalType,
                     e.principal_id AS PrincipalId, e.access_type AS AccessType,
                     e.permission AS Permission, e.inherit AS Inherit,
                     e.position AS Position, e.created_at AS CreatedAt
              FROM RS_ACE e
              INNER JOIN RS_ACL a ON a.id = e.acl_id
              WHERE a.entity_type = @EntityType AND a.entity_id = @EntityId
              ORDER BY e.position ASC",
            new { EntityType = entityType, EntityId = entityId });
        return results.ToList();
    }

    public async Task<int> GetMaxAcePositionAsync(long aclId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<int>(
            "SELECT COALESCE(MAX(position), 0) FROM RS_ACE WHERE acl_id = @AclId",
            new { AclId = aclId });
    }

    // ─── Folk set queries ────────────────────────────────────────────────

    public async Task<List<long>> GetUserGroupIdsAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var ids = await conn.QueryAsync<long>(
            @"SELECT group_id FROM RS_GROUP_MEMBER WHERE user_id = @UserId",
            new { UserId = userId });
        return ids.ToList();
    }

    public async Task<List<long>> GetGroupAncestorIdsAsync(List<long> groupIds)
    {
        if (groupIds.Count == 0) return [];

        using var conn = await _database.GetConnectionAsync();
        var ids = await conn.QueryAsync<long>(
            @"SELECT DISTINCT ancestor_id
              FROM RS_GROUP_CLOSURE
              WHERE descendant_id IN @GroupIds AND ancestor_id NOT IN @GroupIds",
            new { GroupIds = groupIds });
        return ids.ToList();
    }

    public async Task<long?> GetUserPrimaryOuIdAsync(long userId)
    {
        // org_unit_id does not exist in RS_USER - return null
        await Task.CompletedTask;
        return null;
    }

    public async Task<List<long>> GetOuAncestorIdsAsync(long ouId)
    {
        using var conn = await _database.GetConnectionAsync();
        var ids = await conn.QueryAsync<long>(
            @"SELECT ancestor_id
              FROM RS_ORG_UNIT_CLOSURE
              WHERE descendant_id = @OuId AND ancestor_id != @OuId",
            new { OuId = ouId });
        return ids.ToList();
    }

    public async Task<List<long>> GetSecondaryOuIdsAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var ids = await conn.QueryAsync<long>(
            "SELECT ou_id FROM RS_OU_MEMBER WHERE user_id = @UserId",
            new { UserId = userId });
        return ids.ToList();
    }

    // ─── Parent chain for inheritance ────────────────────────────────────

    public async Task<long?> GetReportFolderIdAsync(long reportId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT folder_id FROM RS_REPORT WHERE id = @ReportId",
            new { ReportId = reportId });
    }

    public async Task<long?> GetFolderParentIdAsync(long folderId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT parent_id FROM RS_REPORT_FOLDER WHERE id = @FolderId",
            new { FolderId = folderId });
    }

    public async Task<long?> GetUserOuIdAsync(long userId)
    {
        // org_unit_id does not exist in RS_USER - return null
        await Task.CompletedTask;
        return null;
    }

    public async Task<long?> GetGroupOuIdAsync(long groupId)
    {
        // ou_id does not exist in RS_GROUP - return null
        await Task.CompletedTask;
        return null;
    }

    public async Task<long?> GetOuParentIdAsync(long ouId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<long?>(
            "SELECT parent_id FROM RS_ORG_UNIT WHERE id = @OuId",
            new { OuId = ouId });
    }
}
