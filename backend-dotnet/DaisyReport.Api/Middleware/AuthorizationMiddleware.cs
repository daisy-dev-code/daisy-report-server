using DaisyReport.Api.Services;

namespace DaisyReport.Api.Middleware;

/// <summary>
/// Provides extension methods for checking ACL-based permissions in endpoint handlers.
/// Usage in endpoints:
///   var userId = context.GetUserId();
///   var allowed = await aclService.CheckPermissionAsync(userId, "report", reportId, "read");
///   if (!allowed) return Results.Forbid();
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Get the authenticated user's ID from the HttpContext.
    /// Returns null if not authenticated.
    /// </summary>
    public static long? GetUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is long userId)
        {
            return userId;
        }
        return null;
    }

    /// <summary>
    /// Get the authenticated user's role from the HttpContext.
    /// Returns null if not authenticated.
    /// </summary>
    public static string? GetUserRole(this HttpContext context)
    {
        if (context.Items.TryGetValue("UserRole", out var roleObj) && roleObj is string role)
        {
            return role;
        }
        return null;
    }

    /// <summary>
    /// Check if the current user has a specific permission on an entity.
    /// Returns Results.Forbid() if denied, or null if allowed.
    /// Usage:
    ///   var denied = await context.RequirePermissionAsync(aclService, "report", reportId, "read");
    ///   if (denied != null) return denied;
    /// </summary>
    public static async Task<IResult?> RequirePermissionAsync(
        this HttpContext context,
        IAclService aclService,
        string entityType,
        long entityId,
        string permission)
    {
        var userId = context.GetUserId();
        if (userId == null) return Results.Unauthorized();

        var allowed = await aclService.CheckPermissionAsync(userId.Value, entityType, entityId, permission);
        if (!allowed)
        {
            return Results.Json(
                new { error = "Access denied.", entityType, entityId, permission },
                statusCode: 403);
        }

        return null; // Allowed - proceed
    }
}
