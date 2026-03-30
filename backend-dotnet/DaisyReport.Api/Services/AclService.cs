using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Services;

public class AclService : IAclService
{
    private readonly IAclRepository _aclRepo;
    private readonly IUserRepository _userRepo;
    private readonly IRedisCache _cache;
    private readonly ILogger<AclService> _logger;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(10);

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

        // Get role-based permissions
        var rolePerms = await _aclRepo.GetRolePermissionsAsync(user.Role);

        // Get direct user permissions
        var userPerms = await _aclRepo.GetUserPermissionsAsync(userId);

        // Merge (direct overrides role)
        var merged = new HashSet<string>(rolePerms);
        foreach (var perm in userPerms)
        {
            merged.Add(perm);
        }

        var result = merged.ToList();
        await _cache.SetAsync(cacheKey, result, CacheExpiry);
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
}
