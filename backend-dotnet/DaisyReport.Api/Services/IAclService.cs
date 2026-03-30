using DaisyReport.Api.Models;

namespace DaisyReport.Api.Services;

public interface IAclService
{
    // Legacy methods (kept for backward compatibility)
    Task<bool> HasPermissionAsync(long userId, string permission);
    Task<List<string>> GetPermissionsAsync(long userId);
    Task GrantPermissionAsync(long userId, string permission);
    Task RevokePermissionAsync(long userId, string permission);
    Task<List<string>> GetRolePermissionsAsync(string role);

    // Full ACL engine
    Task<bool> CheckPermissionAsync(long userId, string entityType, long entityId, string permission);
    Task<(bool Allowed, string Reason)> CheckPermissionWithReasonAsync(long userId, string entityType, long entityId, string permission);
    Task<List<FolkSetEntry>> GetFolkSetAsync(long userId);
    Task InvalidateUserCacheAsync(long userId);
    Task InvalidateEntityCacheAsync(string entityType, long entityId);
    Task<List<AceEntry>> GetAclAsync(string entityType, long entityId);
    Task<long> AddAceAsync(string entityType, long entityId, string principalType,
        long principalId, string accessType, string permission, bool inherit);
    Task RemoveAceAsync(long aceId);
}
