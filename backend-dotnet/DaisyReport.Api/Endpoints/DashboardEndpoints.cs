using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboards").RequireAuthorization();

        group.MapGet("/", ListDashboards);
        group.MapGet("/{id:long}", GetDashboard);
        group.MapPost("/", CreateDashboard);
        group.MapPut("/{id:long}", UpdateDashboard);
        group.MapDelete("/{id:long}", DeleteDashboard);

        group.MapPost("/{id:long}/dadgets", AddDadget);
        group.MapPut("/{id:long}/dadgets/{dadgetId:long}", UpdateDadget);
        group.MapDelete("/{id:long}/dadgets/{dadgetId:long}", RemoveDadget);
        group.MapPost("/{id:long}/layout", UpdateLayout);

        group.MapGet("/bookmarks", GetBookmarks);
        group.MapPost("/bookmarks", AddBookmark);
        group.MapDelete("/bookmarks/{dashboardId:long}", RemoveBookmark);
    }

    private static async Task<IResult> ListDashboards(
        IDashboardRepository repo,
        int page = 1,
        int pageSize = 25,
        long? folderId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (dashboards, total) = await repo.ListAsync(page, pageSize, folderId);

        return Results.Ok(new
        {
            data = dashboards.Select(d => new
            {
                d.Id,
                d.FolderId,
                d.Name,
                d.Description,
                d.Layout,
                d.Columns,
                d.ReloadInterval,
                d.IsPrimary,
                d.IsConfigProtected,
                d.CreatedBy,
                d.CreatedAt,
                d.UpdatedAt,
                d.Version
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

    private static async Task<IResult> GetDashboard(long id, IDashboardRepository repo)
    {
        var dashboard = await repo.GetByIdAsync(id);
        if (dashboard == null) return Results.NotFound(new { error = "Dashboard not found." });

        return Results.Ok(new
        {
            dashboard.Id,
            dashboard.FolderId,
            dashboard.Name,
            dashboard.Description,
            dashboard.Layout,
            dashboard.Columns,
            dashboard.ReloadInterval,
            dashboard.IsPrimary,
            dashboard.IsConfigProtected,
            dashboard.CreatedBy,
            dashboard.CreatedAt,
            dashboard.UpdatedAt,
            dashboard.Version,
            dadgets = dashboard.Dadgets.Select(d => new
            {
                d.Id,
                d.DashboardId,
                d.Dtype,
                d.ColPosition,
                d.RowPosition,
                d.WidthSpan,
                d.Height,
                d.Config,
                d.CreatedAt
            })
        });
    }

    private static async Task<IResult> CreateDashboard(
        CreateDashboardRequest request,
        IDashboardRepository repo,
        HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        var userId = (long?)context.Items["UserId"] ?? 0;
        var now = DateTime.UtcNow;

        var dashboard = new Dashboard
        {
            FolderId = request.FolderId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Layout = request.Layout ?? "SINGLE",
            Columns = request.Columns ?? 1,
            ReloadInterval = request.ReloadInterval ?? 0,
            IsPrimary = request.IsPrimary ?? false,
            IsConfigProtected = request.IsConfigProtected ?? false,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };

        var id = await repo.CreateAsync(dashboard);

        return Results.Created($"/api/dashboards/{id}", new { id, dashboard.Name });
    }

    private static async Task<IResult> UpdateDashboard(
        long id,
        UpdateDashboardRequest request,
        IDashboardRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Dashboard not found." });

        if (existing.IsConfigProtected)
            return Results.BadRequest(new { error = "Dashboard is config-protected and cannot be modified." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.FolderId.HasValue) existing.FolderId = request.FolderId;
        if (request.Layout != null) existing.Layout = request.Layout;
        if (request.Columns.HasValue) existing.Columns = request.Columns.Value;
        if (request.ReloadInterval.HasValue) existing.ReloadInterval = request.ReloadInterval.Value;
        if (request.IsPrimary.HasValue) existing.IsPrimary = request.IsPrimary.Value;
        if (request.IsConfigProtected.HasValue) existing.IsConfigProtected = request.IsConfigProtected.Value;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update dashboard.");

        return Results.Ok(new { message = "Dashboard updated successfully." });
    }

    private static async Task<IResult> DeleteDashboard(long id, IDashboardRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Dashboard not found." });

        return Results.Ok(new { message = "Dashboard deleted successfully." });
    }

    private static async Task<IResult> AddDadget(
        long id,
        CreateDadgetRequest request,
        IDashboardRepository repo)
    {
        var dashboard = await repo.GetByIdAsync(id);
        if (dashboard == null) return Results.NotFound(new { error = "Dashboard not found." });

        if (string.IsNullOrWhiteSpace(request.Dtype))
            return Results.BadRequest(new { error = "Dtype is required." });

        var dadget = new Dadget
        {
            DashboardId = id,
            Dtype = request.Dtype,
            ColPosition = request.ColPosition ?? 0,
            RowPosition = request.RowPosition ?? 0,
            WidthSpan = request.WidthSpan ?? 1,
            Height = request.Height,
            Config = request.Config ?? "{}",
            CreatedAt = DateTime.UtcNow
        };

        var dadgetId = await repo.AddDadgetAsync(dadget);

        return Results.Created($"/api/dashboards/{id}/dadgets/{dadgetId}", new { id = dadgetId });
    }

    private static async Task<IResult> UpdateDadget(
        long id,
        long dadgetId,
        UpdateDadgetRequest request,
        IDashboardRepository repo)
    {
        var dashboard = await repo.GetByIdAsync(id);
        if (dashboard == null) return Results.NotFound(new { error = "Dashboard not found." });

        var existing = dashboard.Dadgets.FirstOrDefault(d => d.Id == dadgetId);
        if (existing == null) return Results.NotFound(new { error = "Dadget not found." });

        if (request.Dtype != null) existing.Dtype = request.Dtype;
        if (request.ColPosition.HasValue) existing.ColPosition = request.ColPosition.Value;
        if (request.RowPosition.HasValue) existing.RowPosition = request.RowPosition.Value;
        if (request.WidthSpan.HasValue) existing.WidthSpan = request.WidthSpan.Value;
        if (request.Height.HasValue) existing.Height = request.Height.Value;
        if (request.Config != null) existing.Config = request.Config;

        var result = await repo.UpdateDadgetAsync(existing);
        if (!result) return Results.Problem("Failed to update dadget.");

        return Results.Ok(new { message = "Dadget updated successfully." });
    }

    private static async Task<IResult> RemoveDadget(long id, long dadgetId, IDashboardRepository repo)
    {
        var result = await repo.RemoveDadgetAsync(dadgetId);
        if (!result) return Results.NotFound(new { error = "Dadget not found." });

        return Results.Ok(new { message = "Dadget removed successfully." });
    }

    private static async Task<IResult> UpdateLayout(
        long id,
        List<DadgetPosition> positions,
        IDashboardRepository repo)
    {
        var dashboard = await repo.GetByIdAsync(id);
        if (dashboard == null) return Results.NotFound(new { error = "Dashboard not found." });

        await repo.UpdateLayoutAsync(id, positions);

        return Results.Ok(new { message = "Layout updated successfully." });
    }

    private static async Task<IResult> GetBookmarks(IDashboardRepository repo, HttpContext context)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        var bookmarks = await repo.GetBookmarksAsync(userId);

        return Results.Ok(new
        {
            data = bookmarks.Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                d.Layout,
                d.IsPrimary
            })
        });
    }

    private static async Task<IResult> AddBookmark(
        AddBookmarkRequest request,
        IDashboardRepository repo,
        HttpContext context)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        var result = await repo.AddBookmarkAsync(userId, request.DashboardId);

        return Results.Ok(new { message = "Bookmark added successfully." });
    }

    private static async Task<IResult> RemoveBookmark(
        long dashboardId,
        IDashboardRepository repo,
        HttpContext context)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        var result = await repo.RemoveBookmarkAsync(userId, dashboardId);
        if (!result) return Results.NotFound(new { error = "Bookmark not found." });

        return Results.Ok(new { message = "Bookmark removed successfully." });
    }
}

public record CreateDashboardRequest(
    string Name,
    string? Description,
    long? FolderId,
    string? Layout,
    int? Columns,
    int? ReloadInterval,
    bool? IsPrimary,
    bool? IsConfigProtected);

public record UpdateDashboardRequest(
    string? Name,
    string? Description,
    long? FolderId,
    string? Layout,
    int? Columns,
    int? ReloadInterval,
    bool? IsPrimary,
    bool? IsConfigProtected);

public record CreateDadgetRequest(
    string Dtype,
    int? ColPosition,
    int? RowPosition,
    int? WidthSpan,
    int? Height,
    string? Config);

public record UpdateDadgetRequest(
    string? Dtype,
    int? ColPosition,
    int? RowPosition,
    int? WidthSpan,
    int? Height,
    string? Config);

public record AddBookmarkRequest(long DashboardId);
