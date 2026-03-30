using DaisyReport.Api.Services;

namespace DaisyReport.Api.Endpoints;

public static class PermissionEndpoints
{
    public static void MapPermissionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/permissions").RequireAuthorization();

        group.MapGet("/check", CheckPermission);
        group.MapGet("/acl/{entityType}/{entityId:long}", GetAcl);
        group.MapPost("/acl/{entityType}/{entityId:long}", AddAce);
        group.MapDelete("/ace/{aceId:long}", RemoveAce);
        group.MapGet("/folkset", GetFolkSet);
        group.MapPost("/invalidate/user/{userId:long}", InvalidateUserCache);
        group.MapPost("/invalidate/entity/{entityType}/{entityId:long}", InvalidateEntityCache);
    }

    /// <summary>
    /// GET /api/permissions/check?entityType=report&entityId=42&permission=read
    /// </summary>
    private static async Task<IResult> CheckPermission(
        string entityType, long entityId, string permission,
        IAclService aclService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        var (allowed, reason) = await aclService.CheckPermissionWithReasonAsync(
            userId.Value, entityType, entityId, permission);

        return Results.Ok(new { allowed, reason });
    }

    /// <summary>
    /// GET /api/permissions/acl/{entityType}/{entityId}
    /// </summary>
    private static async Task<IResult> GetAcl(
        string entityType, long entityId,
        IAclService aclService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        var aces = await aclService.GetAclAsync(entityType, entityId);
        return Results.Ok(new { entityType, entityId, aces });
    }

    /// <summary>
    /// POST /api/permissions/acl/{entityType}/{entityId}
    /// </summary>
    private static async Task<IResult> AddAce(
        string entityType, long entityId,
        AddAceRequest request,
        IAclService aclService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PrincipalType))
            return Results.BadRequest(new { error = "principalType is required." });
        if (string.IsNullOrWhiteSpace(request.AccessType))
            return Results.BadRequest(new { error = "accessType is required." });
        if (string.IsNullOrWhiteSpace(request.Permission))
            return Results.BadRequest(new { error = "permission is required." });

        try
        {
            var aceId = await aclService.AddAceAsync(
                entityType, entityId,
                request.PrincipalType, request.PrincipalId,
                request.AccessType, request.Permission,
                request.Inherit);

            return Results.Created($"/api/permissions/ace/{aceId}", new { aceId });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/permissions/ace/{aceId}
    /// </summary>
    private static async Task<IResult> RemoveAce(
        long aceId,
        IAclService aclService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        try
        {
            await aclService.RemoveAceAsync(aceId);
            return Results.NoContent();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"ACE {aceId} not found." });
        }
    }

    /// <summary>
    /// GET /api/permissions/folkset
    /// Returns the folk set for the currently authenticated user.
    /// </summary>
    private static async Task<IResult> GetFolkSet(
        IAclService aclService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        var folkSet = await aclService.GetFolkSetAsync(userId.Value);
        return Results.Ok(new { userId, folkSet });
    }

    /// <summary>
    /// POST /api/permissions/invalidate/user/{userId}
    /// Invalidate all permission caches for a user (admin operation).
    /// </summary>
    private static async Task<IResult> InvalidateUserCache(
        long userId,
        IAclService aclService, HttpContext context)
    {
        var currentUserId = GetUserId(context);
        if (currentUserId == null) return Results.Unauthorized();

        await aclService.InvalidateUserCacheAsync(userId);
        return Results.Ok(new { message = $"Cache invalidated for user {userId}." });
    }

    /// <summary>
    /// POST /api/permissions/invalidate/entity/{entityType}/{entityId}
    /// Invalidate all permission caches for an entity (admin operation).
    /// </summary>
    private static async Task<IResult> InvalidateEntityCache(
        string entityType, long entityId,
        IAclService aclService, HttpContext context)
    {
        var currentUserId = GetUserId(context);
        if (currentUserId == null) return Results.Unauthorized();

        await aclService.InvalidateEntityCacheAsync(entityType, entityId);
        return Results.Ok(new { message = $"Cache invalidated for {entityType}:{entityId}." });
    }

    private static long? GetUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is long userId)
        {
            return userId;
        }
        return null;
    }
}

public record AddAceRequest(
    string PrincipalType,
    long PrincipalId,
    string AccessType,
    string Permission,
    bool Inherit = true);
