using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly IDatabase _database;

    public NotificationRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<(List<Notification> Notifications, int Total)> GetByUserAsync(long userId, bool? unreadOnly, int page, int pageSize)
    {
        using var conn = await _database.GetConnectionAsync();

        var conditions = new List<string> { "user_id = @UserId" };
        if (unreadOnly == true)
            conditions.Add("read_flag = 0");

        var whereClause = "WHERE " + string.Join(" AND ", conditions);
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_NOTIFICATIONS {whereClause}",
            new { UserId = userId });

        var notifications = (await conn.QueryAsync<Notification>(
            $@"SELECT id AS Id, user_id AS UserId, type AS Type, title AS Title,
                      message AS Message, read_flag AS ReadFlag, link AS Link,
                      created_at AS CreatedAt
               FROM RS_NOTIFICATIONS {whereClause}
               ORDER BY created_at DESC
               LIMIT @PageSize OFFSET @Offset",
            new { UserId = userId, PageSize = pageSize, Offset = offset })).ToList();

        return (notifications, total);
    }

    public async Task<int> GetUnreadCountAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM RS_NOTIFICATIONS WHERE user_id = @UserId AND read_flag = 0",
            new { UserId = userId });
    }

    public async Task<bool> MarkAsReadAsync(long notificationId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE RS_NOTIFICATIONS SET read_flag = 1 WHERE id = @Id",
            new { Id = notificationId });
        return rows > 0;
    }

    public async Task<int> MarkAllAsReadAsync(long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.ExecuteAsync(
            "UPDATE RS_NOTIFICATIONS SET read_flag = 1 WHERE user_id = @UserId AND read_flag = 0",
            new { UserId = userId });
    }

    public async Task<long> CreateAsync(Notification notification)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_NOTIFICATIONS (user_id, type, title, message, read_flag, link, created_at)
              VALUES (@UserId, @Type, @Title, @Message, @ReadFlag, @Link, @CreatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                notification.UserId,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.ReadFlag,
                notification.Link,
                notification.CreatedAt
            });
        return id;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_NOTIFICATIONS WHERE id = @Id",
            new { Id = id });
        return rows > 0;
    }
}
