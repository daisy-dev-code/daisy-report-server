using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/system/config").RequireAuthorization();

        group.MapGet("/", ListConfig);
        group.MapGet("/{key}", GetConfig);
        group.MapPut("/{key}", SetConfig);
        group.MapDelete("/{key}", DeleteConfig);
    }

    private static async Task<IResult> ListConfig(
        IConfigRepository configRepo,
        string? category = null)
    {
        var entries = await configRepo.ListAsync(category);

        return Results.Ok(new
        {
            data = entries.Select(e => new
            {
                e.Id,
                e.ConfigKey,
                e.ConfigValue,
                e.Category,
                e.Description,
                e.UpdatedAt
            })
        });
    }

    private static async Task<IResult> GetConfig(string key, IConfigRepository configRepo)
    {
        var entry = await configRepo.GetByKeyAsync(key);
        if (entry == null) return Results.NotFound(new { error = "Config key not found." });

        return Results.Ok(new
        {
            entry.Id,
            entry.ConfigKey,
            entry.ConfigValue,
            entry.Category,
            entry.Description,
            entry.UpdatedAt
        });
    }

    private static async Task<IResult> SetConfig(string key, SetConfigRequest request, IConfigRepository configRepo)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
            return Results.BadRequest(new { error = "Value is required." });

        var result = await configRepo.SetAsync(key, request.Value, request.Category, request.Description);

        return Results.Ok(new { message = "Config updated successfully.", key });
    }

    private static async Task<IResult> DeleteConfig(string key, IConfigRepository configRepo)
    {
        var result = await configRepo.DeleteAsync(key);
        if (!result) return Results.NotFound(new { error = "Config key not found." });

        return Results.Ok(new { message = "Config deleted successfully." });
    }
}

public record SetConfigRequest(string Value, string? Category, string? Description);
