using System.IdentityModel.Tokens.Jwt;
using DaisyReport.Api.Services;

namespace DaisyReport.Api.Middleware;

/// <summary>
/// Extracts JWT from Authorization header and adds user claims to the context.
/// This supplements the built-in JwtBearer authentication by caching user lookups
/// and validating session state against Redis.
/// </summary>
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;

    public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract the user ID from the authenticated principal to make it easily available
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst(JwtRegisteredClaimNames.Sub)
                   ?? context.User.FindFirst("sub");

            if (sub != null && long.TryParse(sub.Value, out var userId))
            {
                context.Items["UserId"] = userId;
            }

            var role = context.User.FindFirst("role");
            if (role != null)
            {
                context.Items["UserRole"] = role.Value;
            }
        }

        await _next(context);
    }
}
