using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class DatasourceEndpoints
{
    public static void MapDatasourceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/datasources").RequireAuthorization();

        group.MapGet("/", ListDatasources);
        group.MapGet("/{id:long}", GetDatasource);
        group.MapPost("/", CreateDatasource);
        group.MapPut("/{id:long}", UpdateDatasource);
        group.MapDelete("/{id:long}", DeleteDatasource);
        group.MapPost("/{id:long}/test", TestConnection);
    }

    private static async Task<IResult> ListDatasources(IDatasourceRepository repo)
    {
        var datasources = await repo.ListAsync();

        return Results.Ok(new
        {
            data = datasources.Select(d => new
            {
                d.Id,
                d.Name,
                d.Description,
                d.Dtype,
                d.FolderId,
                d.DriverClass,
                d.JdbcUrl,
                d.Username,
                d.MinPool,
                d.MaxPool,
                d.QueryTimeout,
                d.CreatedAt,
                d.UpdatedAt
            })
        });
    }

    private static async Task<IResult> GetDatasource(long id, IDatasourceRepository repo)
    {
        var ds = await repo.GetByIdAsync(id);
        if (ds == null) return Results.NotFound(new { error = "Datasource not found." });

        return Results.Ok(new
        {
            ds.Id,
            ds.Name,
            ds.Description,
            ds.Dtype,
            ds.FolderId,
            ds.DriverClass,
            ds.JdbcUrl,
            ds.Username,
            ds.MinPool,
            ds.MaxPool,
            ds.QueryTimeout,
            ds.CreatedAt,
            ds.UpdatedAt
        });
    }

    private static async Task<IResult> CreateDatasource(CreateDatasourceRequest request, IDatasourceRepository repo)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Name is required." });
        }

        var datasource = new Datasource
        {
            Name = request.Name,
            Description = request.Description,
            Dtype = request.Dtype ?? "database",
            FolderId = request.FolderId,
            DriverClass = request.DriverClass,
            JdbcUrl = request.JdbcUrl,
            Username = request.Username,
            PasswordEncrypted = request.PasswordEncrypted,
            MinPool = request.MinPool,
            MaxPool = request.MaxPool,
            QueryTimeout = request.QueryTimeout,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var id = await repo.CreateAsync(datasource);

        return Results.Created($"/api/datasources/{id}", new { id, datasource.Name });
    }

    private static async Task<IResult> UpdateDatasource(long id, UpdateDatasourceRequest request, IDatasourceRepository repo)
    {
        var existing = await repo.GetByIdAsync(id);
        if (existing == null) return Results.NotFound(new { error = "Datasource not found." });

        if (request.Name != null) existing.Name = request.Name;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Dtype != null) existing.Dtype = request.Dtype;
        if (request.FolderId.HasValue) existing.FolderId = request.FolderId;
        if (request.DriverClass != null) existing.DriverClass = request.DriverClass;
        if (request.JdbcUrl != null) existing.JdbcUrl = request.JdbcUrl;
        if (request.Username != null) existing.Username = request.Username;
        if (request.PasswordEncrypted != null) existing.PasswordEncrypted = request.PasswordEncrypted;
        if (request.MinPool.HasValue) existing.MinPool = request.MinPool;
        if (request.MaxPool.HasValue) existing.MaxPool = request.MaxPool;
        if (request.QueryTimeout.HasValue) existing.QueryTimeout = request.QueryTimeout;
        existing.UpdatedAt = DateTime.UtcNow;

        var result = await repo.UpdateAsync(existing);
        if (!result) return Results.Problem("Failed to update datasource.");

        return Results.Ok(new { message = "Datasource updated successfully." });
    }

    private static async Task<IResult> DeleteDatasource(long id, IDatasourceRepository repo)
    {
        var result = await repo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Datasource not found." });

        return Results.Ok(new { message = "Datasource deleted successfully." });
    }

    private static async Task<IResult> TestConnection(long id, IDatasourceRepository repo)
    {
        var (success, message) = await repo.TestConnectionAsync(id);

        return success
            ? Results.Ok(new { success, message })
            : Results.BadRequest(new { success, message });
    }
}

public record CreateDatasourceRequest(
    string Name,
    string? Description,
    string? Dtype,
    long? FolderId,
    string? DriverClass,
    string? JdbcUrl,
    string? Username,
    string? PasswordEncrypted,
    int? MinPool,
    int? MaxPool,
    int? QueryTimeout);

public record UpdateDatasourceRequest(
    string? Name,
    string? Description,
    string? Dtype,
    long? FolderId,
    string? DriverClass,
    string? JdbcUrl,
    string? Username,
    string? PasswordEncrypted,
    int? MinPool,
    int? MaxPool,
    int? QueryTimeout);
