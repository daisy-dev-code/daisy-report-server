using DaisyReport.Api.Services;

namespace DaisyReport.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/logout", Logout);
        group.MapGet("/session", GetSession);
        group.MapPost("/change-password", ChangePassword);
    }

    private static async Task<IResult> Login(LoginRequest request, IUserService userService, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "Username and password are required." });
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();

        var result = await userService.LoginAsync(request.Username, request.Password, ipAddress, userAgent);
        if (result == null)
        {
            return Results.Unauthorized();
        }

        var (user, token) = result.Value;
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new
        {
            token,
            user = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email ?? "",
                firstname = user.Firstname ?? "",
                lastname = user.Lastname ?? ""
            }
        });
    }

    private static async Task<IResult> Logout(IUserService userService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        var token = context.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
        await userService.LogoutAsync(userId.Value, token);

        return Results.Ok(new { message = "Logged out successfully." });
    }

    private static async Task<IResult> GetSession(IUserService userService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        var user = await userService.GetSessionUserAsync(userId.Value);
        if (user == null) return Results.Unauthorized();

        return Results.Ok(new
        {
            user = new
            {
                id = user.Id,
                username = user.Username,
                email = user.Email ?? "",
                firstname = user.Firstname ?? "",
                lastname = user.Lastname ?? ""
            }
        });
    }

    private static async Task<IResult> ChangePassword(ChangePasswordRequest request, IUserService userService, HttpContext context)
    {
        var userId = GetUserId(context);
        if (userId == null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Results.BadRequest(new { error = "Current password and new password are required." });
        }

        if (request.NewPassword.Length < 8)
        {
            return Results.BadRequest(new { error = "New password must be at least 8 characters." });
        }

        var result = await userService.ChangePasswordAsync(userId.Value, request.CurrentPassword, request.NewPassword);
        if (!result)
        {
            return Results.BadRequest(new { error = "Current password is incorrect." });
        }

        return Results.Ok(new { message = "Password changed successfully." });
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

public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
