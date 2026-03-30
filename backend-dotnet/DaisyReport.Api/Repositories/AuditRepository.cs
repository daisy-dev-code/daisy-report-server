using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly IDatabase _database;

    public AuditRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task LogAsync(long? userId, string action, string? entityType, long? entityId, string? details, string? ipAddress)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO RS_AUDIT_LOG (user_id, action, entity_type, entity_id, details, ip_address, created_at)
              VALUES (@UserId, @Action, @EntityType, @EntityId, @Details, @IpAddress, @CreatedAt)",
            new
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow
            });
    }

    public async Task<(List<AuditLog> Logs, int Total)> ListAsync(int page, int pageSize, long? userId, string? action)
    {
        using var conn = await _database.GetConnectionAsync();

        var conditions = new List<string>();
        if (userId.HasValue) conditions.Add("user_id = @UserId");
        if (!string.IsNullOrWhiteSpace(action)) conditions.Add("action = @Action");

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_AUDIT_LOG {whereClause}",
            new { UserId = userId, Action = action });

        var logs = (await conn.QueryAsync<AuditLog>(
            $@"SELECT id AS Id, user_id AS UserId, action AS Action, entity_type AS EntityType,
                      entity_id AS EntityId, details AS Details, ip_address AS IpAddress,
                      created_at AS CreatedAt
               FROM RS_AUDIT_LOG {whereClause}
               ORDER BY created_at DESC
               LIMIT @PageSize OFFSET @Offset",
            new { UserId = userId, Action = action, PageSize = pageSize, Offset = offset })).ToList();

        return (logs, total);
    }
}
