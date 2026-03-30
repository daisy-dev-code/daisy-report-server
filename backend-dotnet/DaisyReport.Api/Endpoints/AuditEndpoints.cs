using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit").RequireAuthorization();

        group.MapGet("/", ListAuditLogs);
        group.MapGet("/{id:long}", GetAuditEntry);
    }

    private static async Task<IResult> ListAuditLogs(
        IAuditRepository auditRepo,
        int page = 1,
        int pageSize = 25,
        string? action = null,
        string? username = null,
        string? entityType = null,
        long? userId = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (logs, total) = await auditRepo.ListAsync(page, pageSize, userId, action, entityType, username, dateFrom, dateTo);

        return Results.Ok(new
        {
            data = logs.Select(l => new
            {
                l.Id,
                l.UserId,
                l.Action,
                l.EntityType,
                l.EntityId,
                l.Details,
                l.IpAddress,
                l.CreatedAt
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

    private static async Task<IResult> GetAuditEntry(long id, IAuditRepository auditRepo)
    {
        var entry = await auditRepo.GetByIdAsync(id);
        if (entry == null) return Results.NotFound(new { error = "Audit entry not found." });

        return Results.Ok(new
        {
            entry.Id,
            entry.UserId,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.Details,
            entry.IpAddress,
            entry.CreatedAt
        });
    }
}
