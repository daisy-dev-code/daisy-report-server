using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class ReportFolderEndpoints
{
    public static void MapReportFolderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/report-folders").RequireAuthorization();

        group.MapGet("/", GetTree);
        group.MapPost("/", CreateFolder);
        group.MapPut("/{id:long}", UpdateFolder);
        group.MapDelete("/{id:long}", DeleteFolder);
    }

    private static async Task<IResult> GetTree(IReportFolderRepository repo)
    {
        var folders = await repo.GetTreeAsync();

        return Results.Ok(new
        {
            data = folders.Select(f => new
            {
                f.Id,
                f.Name,
                f.ParentId,
                f.Description,
                f.CreatedAt
            })
        });
    }

    private static async Task<IResult> CreateFolder(CreateReportFolderRequest request, IReportFolderRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var folder = new ReportFolder
        {
            Name = request.Name,
            ParentId = request.ParentId,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(folder);

        return Results.Created($"/api/report-folders/{id}", new { id, folder.Name });
    }

    private static async Task<IResult> UpdateFolder(long id, UpdateReportFolderRequest request, IReportFolderRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Report folder not found." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.ParentId.HasValue) existing.ParentId = request.ParentId;
        if (request.Description != null) existing.Description = request.Description;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update report folder.");

        return Results.Ok(new { message = "Report folder updated successfully." });
    }

    private static async Task<IResult> DeleteFolder(long id, IReportFolderRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Report folder not found." });

        return Results.Ok(new { message = "Report folder deleted successfully." });
    }
}

public record CreateReportFolderRequest(string Name, long? ParentId, string? Description);
public record UpdateReportFolderRequest(string? Name, long? ParentId, string? Description);
