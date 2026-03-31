using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.SpreadsheetServer.Distribution.Models;

namespace DaisyReport.Api.SpreadsheetServer.Distribution.Endpoints;

public static class DistributionEndpoints
{
    public static void MapDistributionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/spreadsheet").RequireAuthorization();

        // Template management
        group.MapPost("/templates", UploadTemplate).DisableAntiforgery();
        group.MapGet("/templates", ListTemplates);
        group.MapGet("/templates/{id:long}", DownloadTemplate);
        group.MapDelete("/templates/{id:long}", DeleteTemplate);

        // Distribution
        group.MapPost("/distribute", ExecuteDistribution);
        group.MapPost("/distribute/preview", PreviewDistribution);
    }

    // ── Template Upload (multipart form) ─────────────────────────────────────────

    private static async Task<IResult> UploadTemplate(
        HttpRequest request,
        IDatabase database)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart form data." });

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
            return Results.BadRequest(new { error = "No file uploaded. Use form field 'file'." });

        var name = form["name"].FirstOrDefault() ?? Path.GetFileNameWithoutExtension(file.FileName);
        var description = form["description"].FirstOrDefault() ?? "";
        var defaultConnectionIdStr = form["default_connection_id"].FirstOrDefault();
        long defaultConnectionId = 0;
        if (!string.IsNullOrWhiteSpace(defaultConnectionIdStr))
            long.TryParse(defaultConnectionIdStr, out defaultConnectionId);

        // Read file bytes
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var content = ms.ToArray();

        // Validate it's a valid Excel file
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Only .xlsx and .xls files are supported." });
        }

        using var conn = await database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_EXCEL_TEMPLATE (name, description, file_name, file_content, file_size,
                     content_type, default_connection_id, created_at, updated_at)
              VALUES (@Name, @Description, @FileName, @Content, @FileSize,
                     @ContentType, @DefaultConnectionId, NOW(), NOW());
              SELECT LAST_INSERT_ID();",
            new
            {
                Name = name,
                Description = description,
                FileName = file.FileName,
                Content = content,
                FileSize = content.Length,
                ContentType = file.ContentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                DefaultConnectionId = defaultConnectionId
            });

        return Results.Created($"/api/spreadsheet/templates/{id}", new
        {
            id,
            name,
            fileName = file.FileName,
            fileSize = content.Length,
            message = "Template uploaded successfully."
        });
    }

    // ── List Templates ───────────────────────────────────────────────────────────

    private static async Task<IResult> ListTemplates(IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();
        var templates = await conn.QueryAsync<dynamic>(
            @"SELECT id, name, description, file_name AS fileName, file_size AS fileSize,
                     content_type AS contentType, default_connection_id AS defaultConnectionId,
                     created_at AS createdAt, updated_at AS updatedAt
              FROM RS_EXCEL_TEMPLATE
              ORDER BY updated_at DESC");

        return Results.Ok(new { data = templates });
    }

    // ── Download Template ────────────────────────────────────────────────────────

    private static async Task<IResult> DownloadTemplate(long id, IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();
        var template = await conn.QuerySingleOrDefaultAsync<dynamic>(
            @"SELECT file_name AS fileName, file_content AS content, content_type AS contentType
              FROM RS_EXCEL_TEMPLATE WHERE id = @Id",
            new { Id = id });

        if (template == null)
            return Results.NotFound(new { error = "Template not found." });

        var fileName = (string?)template.fileName ?? "template.xlsx";
        var contentType = (string?)template.contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var content = (byte[])template.content;

        return Results.File(content, contentType, fileName);
    }

    // ── Delete Template ──────────────────────────────────────────────────────────

    private static async Task<IResult> DeleteTemplate(long id, IDatabase database)
    {
        using var conn = await database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync("DELETE FROM RS_EXCEL_TEMPLATE WHERE id = @Id", new { Id = id });

        return rows > 0
            ? Results.Ok(new { message = "Template deleted." })
            : Results.NotFound(new { error = "Template not found." });
    }

    // ── Execute Distribution ─────────────────────────────────────────────────────

    private static async Task<IResult> ExecuteDistribution(
        DistributionConfig config,
        IDistributionEngine engine,
        CancellationToken ct)
    {
        if (config.TemplateId <= 0)
            return Results.BadRequest(new { error = "TemplateId is required." });

        var result = await engine.ExecuteAsync(config, ct);
        return result.Success
            ? Results.Ok(result)
            : Results.UnprocessableEntity(result);
    }

    // ── Preview Distribution ─────────────────────────────────────────────────────

    private static async Task<IResult> PreviewDistribution(
        DistributionConfig config,
        IDistributionEngine engine,
        CancellationToken ct)
    {
        if (config.TemplateId <= 0)
            return Results.BadRequest(new { error = "TemplateId is required." });

        var result = await engine.PreviewAsync(config, ct);
        return result.Success
            ? Results.Ok(result)
            : Results.UnprocessableEntity(result);
    }
}
