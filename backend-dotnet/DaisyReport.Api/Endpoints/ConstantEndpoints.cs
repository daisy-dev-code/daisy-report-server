using Dapper;
using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.Endpoints;

public static class ConstantEndpoints
{
    public static void MapConstantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/constants").RequireAuthorization();

        group.MapGet("/", ListConstants);
        group.MapPost("/", CreateConstant);
        group.MapPut("/{id:long}", UpdateConstant);
        group.MapDelete("/{id:long}", DeleteConstant);
    }

    private static async Task<IResult> ListConstants(IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();
        var constants = await conn.QueryAsync(
            @"SELECT id AS Id, name AS Name, value AS Value, description AS Description,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_GLOBAL_CONSTANT
              ORDER BY name");

        return Results.Ok(new { data = constants });
    }

    private static async Task<IResult> CreateConstant(
        CreateConstantRequest request,
        IDatabase database)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.BadRequest(new { error = "Name is required." });

        using var conn = await database.GetConnectionAsync();
        var now = DateTime.UtcNow;

        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_GLOBAL_CONSTANT (name, value, description, created_at, updated_at)
              VALUES (@Name, @Value, @Description, @Now, @Now);
              SELECT LAST_INSERT_ID();",
            new
            {
                request.Name,
                Value = request.Value ?? "",
                request.Description,
                Now = now
            });

        return Results.Created($"/api/constants/{id}", new { id, request.Name });
    }

    private static async Task<IResult> UpdateConstant(
        long id,
        UpdateConstantRequest request,
        IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();

        var existing = await conn.QuerySingleOrDefaultAsync(
            "SELECT id FROM RS_GLOBAL_CONSTANT WHERE id = @Id",
            new { Id = id });

        if (existing == null)
            return Results.NotFound(new { error = "Constant not found." });

        await conn.ExecuteAsync(
            @"UPDATE RS_GLOBAL_CONSTANT SET
                name = COALESCE(@Name, name),
                value = COALESCE(@Value, value),
                description = COALESCE(@Description, description),
                updated_at = @Now
              WHERE id = @Id",
            new
            {
                Id = id,
                request.Name,
                request.Value,
                request.Description,
                Now = DateTime.UtcNow
            });

        return Results.Ok(new { message = "Constant updated successfully." });
    }

    private static async Task<IResult> DeleteConstant(long id, IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_GLOBAL_CONSTANT WHERE id = @Id",
            new { Id = id });

        if (rows == 0)
            return Results.NotFound(new { error = "Constant not found." });

        return Results.Ok(new { message = "Constant deleted successfully." });
    }
}

public record CreateConstantRequest(string Name, string? Value, string? Description);
public record UpdateConstantRequest(string? Name, string? Value, string? Description);
