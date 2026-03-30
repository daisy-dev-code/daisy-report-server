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
            @"INSERT INTO RS_AUDIT_LOG (username, action, entity_type, entity_id, details, remote_addr, created_at)
              VALUES (@Username, @Action, @EntityType, @EntityId, CASE WHEN @Details IS NOT NULL THEN JSON_OBJECT('message', @Details) ELSE NULL END, @RemoteAddr, @CreatedAt)",
            new
            {
                Username = userId?.ToString(),
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                RemoteAddr = ipAddress,
                CreatedAt = DateTime.UtcNow
            });
    }

    public async Task<(List<AuditLog> Logs, int Total)> ListAsync(int page, int pageSize, long? userId, string? action,
        string? entityType = null, string? username = null, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        using var conn = await _database.GetConnectionAsync();

        var conditions = new List<string>();
        var useJoin = !string.IsNullOrWhiteSpace(username);

        if (userId.HasValue) conditions.Add("a.username = @UserId");
        if (!string.IsNullOrWhiteSpace(action)) conditions.Add("a.action = @Action");
        if (!string.IsNullOrWhiteSpace(entityType)) conditions.Add("a.entity_type = @EntityType");
        if (dateFrom.HasValue) conditions.Add("a.created_at >= @DateFrom");
        if (dateTo.HasValue) conditions.Add("a.created_at <= @DateTo");
        if (useJoin) conditions.Add("u.username LIKE @Username");

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        var joinClause = useJoin
            ? "LEFT JOIN RS_USER u ON a.username = CAST(u.id AS CHAR)"
            : "";

        var offset = (page - 1) * pageSize;
        var parameters = new
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Username = useJoin ? $"%{username}%" : null,
            PageSize = pageSize,
            Offset = offset
        };

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_AUDIT_LOG a {joinClause} {whereClause}",
            parameters);

        var logs = (await conn.QueryAsync<AuditLog>(
            $@"SELECT a.id AS Id, a.username AS UserId, a.action AS Action, a.entity_type AS EntityType,
                      a.entity_id AS EntityId, a.details AS Details, a.remote_addr AS IpAddress,
                      a.created_at AS CreatedAt
               FROM RS_AUDIT_LOG a {joinClause} {whereClause}
               ORDER BY a.created_at DESC
               LIMIT @PageSize OFFSET @Offset",
            parameters)).ToList();

        return (logs, total);
    }

    public async Task<AuditLog?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<AuditLog>(
            @"SELECT id AS Id, username AS UserId, action AS Action, entity_type AS EntityType,
                     entity_id AS EntityId, details AS Details, remote_addr AS IpAddress,
                     created_at AS CreatedAt
              FROM RS_AUDIT_LOG WHERE id = @Id",
            new { Id = id });
    }
}
