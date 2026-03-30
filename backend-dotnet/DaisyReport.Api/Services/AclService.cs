using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Services;

public class AclService : IAclService
{
    private readonly IAclRepository _aclRepo;
    private readonly IUserRepository _userRepo;
    private readonly IRedisCache _cache;
    private readonly ILogger<AclService> _logger;

    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);
    private static readonly TimeSpan LegacyCacheExpiry = TimeSpan.FromMinutes(10);

    // Maximum depth for parent chain walk to prevent infinite loops
    private const int MaxInheritanceDepth = 50;

    public AclService(
        IAclRepository aclRepo,
        IUserRepository userRepo,
        IRedisCache cache,
        ILogger<AclService> logger)
    {
        _aclRepo = aclRepo;
        _userRepo = userRepo;
        _cache = cache;
        _logger = logger;
    }

    // ─── Full ACL Engine ─────────────────────────────────────────────────

    /// <summary>
    /// Check whether a user has a specific permission on an entity.
    /// Uses folk set resolution, ACE chain evaluation, and parent inheritance.
    /// </summary>
    public async Task<bool> CheckPermissionAsync(long userId, string entityType, long entityId, string permission)
    {
        var (allowed, _) = await CheckPermissionWithReasonAsync(userId, entityType, entityId, permission);
        return allowed;
    }

    /// <summary>
    /// Check permission and return the reason string (useful for diagnostics/API responses).
    /// </summary>
    public async Task<(bool Allowed, string Reason)> CheckPermissionWithReasonAsync(
        long userId, string entityType, long entityId, string permission)
    {
        // Check permission cache first
        var permCacheKey = $"perm:{userId}:{entityType}:{entityId}:{permission}";
        var cached = await _cache.GetAsync<PermissionCacheEntry>(permCacheKey);
        if (cached != null)
        {
            return (cached.Allowed, cached.Reason);
        }

        // Resolve the user's folk set
        var folkSet = await GetFolkSetAsync(userId);

        // Walk the entity and its parent chain
        var result = await EvaluateChainAsync(folkSet, entityType, entityId, permission, 0);

        // Cache the result
        var entry = new PermissionCacheEntry { Allowed = result.Allowed, Reason = result.Reason };
        await _cache.SetAsync(permCacheKey, entry, CacheExpiry);

        return result;
    }

    /// <summary>
    /// Resolve the full folk set (all principals) for a user.
    /// Cached in Redis with key folkset:{userId}.
    /// </summary>
    public async Task<List<FolkSetEntry>> GetFolkSetAsync(long userId)
    {
        var cacheKey = $"folkset:{userId}";
        var cached = await _cache.GetAsync<List<FolkSetEntry>>(cacheKey);
        if (cached != null) return cached;

        var folkSet = new List<FolkSetEntry>();
        var seen = new HashSet<string>(); // "TYPE:ID" to avoid duplicates

        void Add(string principalType, long principalId)
        {
            var key = $"{principalType}:{principalId}";
            if (seen.Add(key))
            {
                folkSet.Add(new FolkSetEntry { PrincipalType = principalType, PrincipalId = principalId });
            }
        }

        // 1. The user's own ID
        Add("USER", userId);

        // 2. Direct group memberships
        var directGroups = await _aclRepo.GetUserGroupIdsAsync(userId);
        foreach (var gid in directGroups)
        {
            Add("GROUP", gid);
        }

        // 3. Ancestor groups via transitive closure
        if (directGroups.Count > 0)
        {
            var ancestorGroups = await _aclRepo.GetGroupAncestorIdsAsync(directGroups);
            foreach (var gid in ancestorGroups)
            {
                Add("GROUP", gid);
            }
        }

        // 4. Primary OU
        var primaryOuId = await _aclRepo.GetUserPrimaryOuIdAsync(userId);
        if (primaryOuId.HasValue)
        {
            Add("OU", primaryOuId.Value);

            // 5. Ancestor OUs of primary
            var ouAncestors = await _aclRepo.GetOuAncestorIdsAsync(primaryOuId.Value);
            foreach (var ouId in ouAncestors)
            {
                Add("OU", ouId);
            }
        }

        // 6. Secondary OU memberships
        var secondaryOus = await _aclRepo.GetSecondaryOuIdsAsync(userId);
        foreach (var ouId in secondaryOus)
        {
            Add("OU", ouId);

            // Also include ancestors of secondary OUs
            var secOuAncestors = await _aclRepo.GetOuAncestorIdsAsync(ouId);
            foreach (var ancestorOuId in secOuAncestors)
            {
                Add("OU", ancestorOuId);
            }
        }

        await _cache.SetAsync(cacheKey, folkSet, CacheExpiry);
        _logger.LogDebug("Resolved folk set for user {UserId}: {Count} principals", userId, folkSet.Count);
        return folkSet;
    }

    /// <summary>
    /// Get all ACEs for a specific entity.
    /// </summary>
    public async Task<List<AceEntry>> GetAclAsync(string entityType, long entityId)
    {
        return await _aclRepo.GetAcesForEntityAsync(entityType, entityId);
    }

    /// <summary>
    /// Add an ACE to an entity's ACL, creating the ACL if it doesn't exist.
    /// Returns the new ACE ID.
    /// </summary>
    public async Task<long> AddAceAsync(string entityType, long entityId, string principalType,
        long principalId, string accessType, string permission, bool inherit)
    {
        // Validate inputs
        accessType = accessType.ToUpperInvariant();
        if (accessType != "GRANT" && accessType != "REVOKE")
        {
            throw new ArgumentException("accessType must be GRANT or REVOKE", nameof(accessType));
        }

        principalType = principalType.ToUpperInvariant();
        if (principalType != "USER" && principalType != "GROUP" && principalType != "OU" && principalType != "WILDCARD")
        {
            throw new ArgumentException("principalType must be USER, GROUP, OU, or WILDCARD", nameof(principalType));
        }

        // Get or create ACL
        var aclId = await _aclRepo.GetAclIdAsync(entityType, entityId);
        if (!aclId.HasValue)
        {
            aclId = await _aclRepo.CreateAclAsync(entityType, entityId);
        }

        // Determine position (append to end)
        var maxPos = await _aclRepo.GetMaxAcePositionAsync(aclId.Value);
        var newPos = maxPos + 10;

        var aceId = await _aclRepo.AddAceAsync(aclId.Value, principalType, principalId,
            accessType, permission, inherit, newPos);

        // Invalidate caches for this entity
        await InvalidateEntityCacheAsync(entityType, entityId);

        _logger.LogInformation(
            "Added ACE {AceId}: {AccessType} {Permission} to {PrincipalType}:{PrincipalId} on {EntityType}:{EntityId}",
            aceId, accessType, permission, principalType, principalId, entityType, entityId);

        return aceId;
    }

    /// <summary>
    /// Remove an ACE by its ID.
    /// </summary>
    public async Task RemoveAceAsync(long aceId)
    {
        var removed = await _aclRepo.RemoveAceAsync(aceId);
        if (!removed)
        {
            throw new KeyNotFoundException($"ACE {aceId} not found");
        }

        // We can't easily know which entity was affected without querying first,
        // but we can invalidate by pattern
        await _cache.RemoveByPatternAsync("perm:*");

        _logger.LogInformation("Removed ACE {AceId}", aceId);
    }

    /// <summary>
    /// Invalidate all cached permissions and folk set for a user.
    /// Call this when group membership, OU membership, or direct permissions change.
    /// </summary>
    public async Task InvalidateUserCacheAsync(long userId)
    {
        await _cache.RemoveAsync($"folkset:{userId}");
        await _cache.RemoveByPatternAsync($"perm:{userId}:*");
        await _cache.RemoveAsync($"acl:user:{userId}:permissions"); // Legacy cache key
        _logger.LogInformation("Invalidated ACL cache for user {UserId}", userId);
    }

    /// <summary>
    /// Invalidate all cached permission results for a specific entity.
    /// Call this when ACLs on an entity change.
    /// </summary>
    public async Task InvalidateEntityCacheAsync(string entityType, long entityId)
    {
        await _cache.RemoveByPatternAsync($"perm:*:{entityType}:{entityId}:*");
        _logger.LogInformation("Invalidated ACL cache for entity {EntityType}:{EntityId}", entityType, entityId);
    }

    // ─── Legacy methods (backward compatibility) ─────────────────────────

    public async Task<bool> HasPermissionAsync(long userId, string permission)
    {
        var permissions = await GetPermissionsAsync(userId);
        return permissions.Contains(permission) || permissions.Contains("*");
    }

    public async Task<List<string>> GetPermissionsAsync(long userId)
    {
        var cacheKey = $"acl:user:{userId}:permissions";
        var cached = await _cache.GetAsync<List<string>>(cacheKey);
        if (cached != null) return cached;

        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) return [];

        var rolePerms = await _aclRepo.GetRolePermissionsAsync(user.Role);
        var userPerms = await _aclRepo.GetUserPermissionsAsync(userId);

        var merged = new HashSet<string>(rolePerms);
        foreach (var perm in userPerms)
        {
            merged.Add(perm);
        }

        var result = merged.ToList();
        await _cache.SetAsync(cacheKey, result, LegacyCacheExpiry);
        return result;
    }

    public async Task GrantPermissionAsync(long userId, string permission)
    {
        await _aclRepo.GrantUserPermissionAsync(userId, permission);
        await _cache.RemoveAsync($"acl:user:{userId}:permissions");
        _logger.LogInformation("Granted permission {Permission} to user {UserId}", permission, userId);
    }

    public async Task RevokePermissionAsync(long userId, string permission)
    {
        await _aclRepo.RevokeUserPermissionAsync(userId, permission);
        await _cache.RemoveAsync($"acl:user:{userId}:permissions");
        _logger.LogInformation("Revoked permission {Permission} from user {UserId}", permission, userId);
    }

    public async Task<List<string>> GetRolePermissionsAsync(string role)
    {
        return await _aclRepo.GetRolePermissionsAsync(role);
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Evaluate the ACL chain for an entity, walking up the parent chain on inheritance.
    /// </summary>
    private async Task<(bool Allowed, string Reason)> EvaluateChainAsync(
        List<FolkSetEntry> folkSet, string entityType, long entityId, string permission, int depth)
    {
        if (depth > MaxInheritanceDepth)
        {
            _logger.LogWarning(
                "ACL inheritance depth exceeded for {EntityType}:{EntityId}", entityType, entityId);
            return (false, "DEFAULT DENY (inheritance depth exceeded)");
        }

        // Get the ACL for this entity
        var aclId = await _aclRepo.GetAclIdAsync(entityType, entityId);
        if (aclId.HasValue)
        {
            // Get all ACEs ordered by position
            var aces = await _aclRepo.GetAcesByAclIdAsync(aclId.Value);

            foreach (var ace in aces)
            {
                // When walking inherited ACLs, only consider ACEs with inherit=true
                if (depth > 0 && !ace.Inherit) continue;

                // Check if the ACE's principal matches any entry in the folk set (or is wildcard)
                var principalMatch = ace.PrincipalType == "WILDCARD" ||
                    folkSet.Any(f => f.PrincipalType == ace.PrincipalType && f.PrincipalId == ace.PrincipalId);

                if (!principalMatch) continue;

                // Check if the ACE's permission matches (or is wildcard)
                var permissionMatch = ace.Permission == "*" || ace.Permission == permission ||
                    PermissionImplies(ace.Permission, permission);

                if (!permissionMatch) continue;

                // We have a match
                var allowed = ace.AccessType == "GRANT";
                var reason = $"{ace.AccessType} via ACE #{ace.Id} on ACL #{aclId.Value} ({entityType}:{entityId})";
                return (allowed, reason);
            }

            // No match in this ACL - check if inheritance is enabled
            var inherit = await _aclRepo.GetAclInheritFlagAsync(aclId.Value);
            if (!inherit)
            {
                return (false, $"DEFAULT DENY (no match, inheritance disabled on ACL #{aclId.Value})");
            }
        }

        // Walk up the parent chain
        var parent = await ResolveParentAsync(entityType, entityId);
        if (parent.HasValue)
        {
            return await EvaluateChainAsync(folkSet, parent.Value.EntityType, parent.Value.EntityId, permission, depth + 1);
        }

        // Reached root with no match: default deny
        return (false, "DEFAULT DENY (no matching ACE in chain)");
    }

    /// <summary>
    /// Resolve the parent entity for inheritance chain walking.
    /// </summary>
    private async Task<(string EntityType, long EntityId)?> ResolveParentAsync(string entityType, long entityId)
    {
        switch (entityType.ToLowerInvariant())
        {
            case "report":
            {
                var folderId = await _aclRepo.GetReportFolderIdAsync(entityId);
                if (folderId.HasValue) return ("folder", folderId.Value);
                break;
            }
            case "folder":
            {
                var parentId = await _aclRepo.GetFolderParentIdAsync(entityId);
                if (parentId.HasValue) return ("folder", parentId.Value);
                break;
            }
            case "user":
            {
                var ouId = await _aclRepo.GetUserOuIdAsync(entityId);
                if (ouId.HasValue) return ("ou", ouId.Value);
                break;
            }
            case "group":
            {
                var ouId = await _aclRepo.GetGroupOuIdAsync(entityId);
                if (ouId.HasValue) return ("ou", ouId.Value);
                break;
            }
            case "ou":
            {
                var parentId = await _aclRepo.GetOuParentIdAsync(entityId);
                if (parentId.HasValue) return ("ou", parentId.Value);
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if an ACE permission implies the requested permission.
    /// Supports hierarchical permissions: "report.*" implies "report.read", "report.write", etc.
    /// </summary>
    private static bool PermissionImplies(string acePermission, string requestedPermission)
    {
        if (acePermission.EndsWith(".*"))
        {
            var prefix = acePermission[..^1]; // "report.*" -> "report."
            return requestedPermission.StartsWith(prefix, StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// Internal cache entry for permission results.
    /// </summary>
    private class PermissionCacheEntry
    {
        public bool Allowed { get; set; }
        public string Reason { get; set; } = "";
    }
}
