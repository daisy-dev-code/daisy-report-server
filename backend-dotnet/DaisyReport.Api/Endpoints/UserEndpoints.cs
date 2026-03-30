using DaisyReport.Api.Models;
using DaisyReport.Api.Services;

namespace DaisyReport.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/", ListUsers);
        group.MapGet("/{id:long}", GetUser);
        group.MapPost("/", CreateUser);
        group.MapPut("/{id:long}", UpdateUser);
        group.MapDelete("/{id:long}", DeleteUser);
    }

    private static async Task<IResult> ListUsers(
        IUserService userService, int page = 1, int pageSize = 25, string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (users, total) = await userService.ListAsync(page, pageSize, search);

        return Results.Ok(new
        {
            items = users.Select(u => new
            {
                u.Id, u.Username, u.Email, u.Firstname, u.Lastname,
                u.DisplayName, u.Enabled, u.CreatedAt
            }),
            totalItems = total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    private static async Task<IResult> GetUser(long id, IUserService userService)
    {
        var user = await userService.GetByIdAsync(id);
        if (user == null) return Results.NotFound(new { error = "User not found." });

        return Results.Ok(new
        {
            user.Id, user.Username, user.Email, user.Firstname, user.Lastname,
            user.DisplayName, user.Enabled, user.CreatedAt, user.UpdatedAt
        });
    }

    private static async Task<IResult> CreateUser(CreateUserRequest request, IUserService userService)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { error = "Username and password are required." });
        if (request.Password.Length < 8)
            return Results.BadRequest(new { error = "Password must be at least 8 characters." });

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            Firstname = request.Firstname,
            Lastname = request.Lastname,
            Enabled = request.Enabled ?? true,
        };

        var id = await userService.CreateAsync(user, request.Password);
        return Results.Created($"/api/users/{id}", new { id, user.Username });
    }

    private static async Task<IResult> UpdateUser(long id, UpdateUserRequest request, IUserService userService)
    {
        var existing = await userService.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "User not found." });

        if (request.Email != null) existing.Email = request.Email;
        if (request.Firstname != null) existing.Firstname = request.Firstname;
        if (request.Lastname != null) existing.Lastname = request.Lastname;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;

        var result = await userService.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update user.");

        return Results.Ok(new { message = "User updated successfully." });
    }

    private static async Task<IResult> DeleteUser(long id, IUserService userService)
    {
        var result = await userService.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "User not found." });
        return Results.Ok(new { message = "User deleted successfully." });
    }
}

public record CreateUserRequest(string Username, string Password, string? Email, string? Firstname, string? Lastname, bool? Enabled);
public record UpdateUserRequest(string? Email, string? Firstname, string? Lastname, bool? Enabled);
