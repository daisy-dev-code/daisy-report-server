using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class DatasinkEndpoints
{
    public static void MapDatasinkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/datasinks").RequireAuthorization();

        group.MapGet("/", ListDatasinks);
        group.MapPost("/", CreateDatasink);
        group.MapPut("/{id:long}", UpdateDatasink);
        group.MapDelete("/{id:long}", DeleteDatasink);
    }

    private static async Task<IResult> ListDatasinks(IDatasinkRepository repo)
    {
        var datasinks = await repo.ListAsync();

        return Results.Ok(new
        {
            data = datasinks.Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                d.Dtype,
                d.FolderId,
                d.CreatedAt
            })
        });
    }

    private static async Task<IResult> CreateDatasink(CreateDatasinkRequest request, IDatasinkRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var datasink = new Datasink
        {
            Name = request.Name,
            Description = request.Description,
            Dtype = request.Dtype ?? "file",
            FolderId = request.FolderId,
            CreatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(datasink);

        return Results.Created($"/api/datasinks/{id}", new { id, datasink.Name });
    }

    private static async Task<IResult> UpdateDatasink(long id, UpdateDatasinkRequest request, IDatasinkRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Datasink not found." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Dtype != null) existing.Dtype = request.Dtype;
        if (request.FolderId.HasValue) existing.FolderId = request.FolderId;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update datasink.");

        return Results.Ok(new { message = "Datasink updated successfully." });
    }

    private static async Task<IResult> DeleteDatasink(long id, IDatasinkRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Datasink not found." });

        return Results.Ok(new { message = "Datasink deleted successfully." });
    }
}

public record CreateDatasinkRequest(string Name, string? Description, string? Dtype, long? FolderId);
public record UpdateDatasinkRequest(string? Name, string? Description, string? Dtype, long? FolderId);
