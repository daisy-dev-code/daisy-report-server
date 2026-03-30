using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class GroupEndpoints
{
    public static void MapGroupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/groups").RequireAuthorization();

        group.MapGet("/", ListGroups);
        group.MapGet("/{id:long}", GetGroup);
        group.MapPost("/", CreateGroup);
        group.MapPut("/{id:long}", UpdateGroup);
        group.MapDelete("/{id:long}", DeleteGroup);
        group.MapGet("/{id:long}/members", GetMembers);
        group.MapPost("/{id:long}/members", AddMember);
        group.MapDelete("/{id:long}/members/{userId:long}", RemoveMember);
    }

    private static async Task<IResult> ListGroups(
        IGroupRepository repo,
        int page = 1,
        int pageSize = 25,
        string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (groups, total) = await repo.ListAsync(page, pageSize, search);

        return Results.Ok(new
        {
            data = groups.Select(g => new
            {
                g.Id,
                g.Name,
                g.Description,
                g.ParentId,
                g.IsActive,
                g.CreatedAt,
                g.UpdatedAt
            }),
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            }
        });
    }

    private static async Task<IResult> GetGroup(long id, IGroupRepository repo)
    {
        var group = await repo.GetByIdAsync(id);
        if (group == null) return Results.NotFound(new { error = "Group not found." });

        return Results.Ok(new
        {
            group.Id,
            group.Name,
            group.Description,
            group.ParentId,
            group.IsActive,
            group.CreatedAt,
            group.UpdatedAt
        });
    }

    private static async Task<IResult> CreateGroup(CreateGroupRequest request, IGroupRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var group = new Group
        {
            Name = request.Name,
            Description = request.Description,
            ParentId = request.ParentId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(group);

        return Results.Created($"/api/groups/{id}", new { id, group.Name });
    }

    private static async Task<IResult> UpdateGroup(long id, UpdateGroupRequest request, IGroupRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Group not found." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.ParentId.HasValue) existing.ParentId = request.ParentId;
        if (request.IsActive.HasValue) existing.IsActive = request.IsActive.Value;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update group.");

        return Results.Ok(new { message = "Group updated successfully." });
    }

    private static async Task<IResult> DeleteGroup(long id, IGroupRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Group not found." });

        return Results.Ok(new { message = "Group deleted successfully." });
    }

    private static async Task<IResult> GetMembers(long id, IGroupRepository repo)
    {
        var group = await repo.GetByIdAsync(id);
        if (group == null) return Results.NotFound(new { error = "Group not found." });

        var members = await repo.GetMembersAsync(id);

        return Results.Ok(new
        {
            data = members.Select(m => new
            {
                m.Id,
                m.Username,
                m.Email,
                m.DisplayName,
                m.Enabled,
                m.CreatedAt
            })
        });
    }

    private static async Task<IResult> AddMember(long id, AddMemberRequest request, IGroupRepository repo)
    {
        var group = await repo.GetByIdAsync(id);
        if (group == null) return Results.NotFound(new { error = "Group not found." });

        var result = await repo.AddMemberAsync(id, request.UserId);

        return Results.Ok(new { message = "Member added successfully." });
    }

    private static async Task<IResult> RemoveMember(long id, long userId, IGroupRepository repo)
    {
        var result = await repo.RemoveMemberAsync(id, userId);
        if (!result) return Results.NotFound(new { error = "Member not found in group." });

        return Results.Ok(new { message = "Member removed successfully." });
    }
}

public record CreateGroupRequest(string Name, string? Description, long? ParentId);
public record UpdateGroupRequest(string? Name, string? Description, long? ParentId, bool? IsActive);
public record AddMemberRequest(long UserId);
