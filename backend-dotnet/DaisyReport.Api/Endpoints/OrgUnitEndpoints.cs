using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class OrgUnitEndpoints
{
    public static void MapOrgUnitEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/org-units").RequireAuthorization();

        group.MapGet("/", GetTree);
        group.MapGet("/{id:long}", GetOrgUnit);
        group.MapPost("/", CreateOrgUnit);
        group.MapPut("/{id:long}", UpdateOrgUnit);
        group.MapDelete("/{id:long}", DeleteOrgUnit);
    }

    private static async Task<IResult> GetTree(IOrgUnitRepository repo)
    {
        var units = await repo.GetTreeAsync();

        return Results.Ok(new
        {
            data = units.Select(u => new
            {
                u.Id,
                u.Name,
                u.Description,
                u.ParentId,
                u.CreatedAt
            })
        });
    }

    private static async Task<IResult> GetOrgUnit(long id, IOrgUnitRepository repo)
    {
        var unit = await repo.GetByIdAsync(id);
        if (unit == null) return Results.NotFound(new { error = "Org unit not found." });

        return Results.Ok(new
        {
            unit.Id,
            unit.Name,
            unit.Description,
            unit.ParentId,
            unit.CreatedAt
        });
    }

    private static async Task<IResult> CreateOrgUnit(CreateOrgUnitRequest request, IOrgUnitRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var unit = new OrgUnit
        {
            Name = request.Name,
            Description = request.Description,
            ParentId = request.ParentId,
            CreatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(unit);

        return Results.Created($"/api/org-units/{id}", new { id, unit.Name });
    }

    private static async Task<IResult> UpdateOrgUnit(long id, UpdateOrgUnitRequest request, IOrgUnitRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Org unit not found." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.ParentId.HasValue) existing.ParentId = request.ParentId;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update org unit.");

        return Results.Ok(new { message = "Org unit updated successfully." });
    }

    private static async Task<IResult> DeleteOrgUnit(long id, IOrgUnitRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Org unit not found." });

        return Results.Ok(new { message = "Org unit deleted successfully." });
    }
}

public record CreateOrgUnitRequest(string Name, string? Description, long? ParentId);
public record UpdateOrgUnitRequest(string? Name, string? Description, long? ParentId);
