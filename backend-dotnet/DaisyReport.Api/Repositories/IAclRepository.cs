using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IAclRepository
{
    // Legacy role/permission operations (kept for backward compatibility)
    Task<List<string>> GetRolePermissionsAsync(string role);
    Task<List<string>> GetUserPermissionsAsync(long userId);
    Task GrantUserPermissionAsync(long userId, string permission);
    Task RevokeUserPermissionAsync(long userId, string permission);

    // ACL operations
    Task<long?> GetAclIdAsync(string entityType, long entityId);
    Task<long> CreateAclAsync(string entityType, long entityId);
    Task<List<AceEntry>> GetAcesByAclIdAsync(long aclId);
    Task<bool> GetAclInheritFlagAsync(long aclId);

    // ACE operations
    Task<long> AddAceAsync(long aclId, string principalType, long principalId,
        string accessType, string permission, bool inherit, int position);
    Task<bool> RemoveAceAsync(long aceId);
    Task<List<AceEntry>> GetAcesForEntityAsync(string entityType, long entityId);
    Task<int> GetMaxAcePositionAsync(long aclId);

    // Folk set queries
    Task<List<long>> GetUserGroupIdsAsync(long userId);
    Task<List<long>> GetGroupAncestorIdsAsync(List<long> groupIds);
    Task<long?> GetUserPrimaryOuIdAsync(long userId);
    Task<List<long>> GetOuAncestorIdsAsync(long ouId);
    Task<List<long>> GetSecondaryOuIdsAsync(long userId);

    // Parent chain for inheritance
    Task<long?> GetReportFolderIdAsync(long reportId);
    Task<long?> GetFolderParentIdAsync(long folderId);
    Task<long?> GetUserOuIdAsync(long userId);
    Task<long?> GetGroupOuIdAsync(long groupId);
    Task<long?> GetOuParentIdAsync(long ouId);
}
