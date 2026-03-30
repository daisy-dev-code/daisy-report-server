using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("/", ListNotifications);
        group.MapGet("/count", GetUnreadCount);
        group.MapPut("/{id:long}/read", MarkAsRead);
        group.MapPut("/read-all", MarkAllAsRead);
        group.MapDelete("/{id:long}", DeleteNotification);
    }

    private static async Task<IResult> ListNotifications(
        INotificationRepository notifRepo,
        HttpContext context,
        bool? unread = null,
        int page = 1,
        int pageSize = 25)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        if (userId == 0) return Results.Unauthorized();

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var (notifications, total) = await notifRepo.GetByUserAsync(userId, unread, page, pageSize);

        return Results.Ok(new
        {
            data = notifications.Select(n => new
            {
                n.Id,
                n.UserId,
                n.Type,
                n.Title,
                n.Message,
                n.ReadFlag,
                n.Link,
                n.CreatedAt
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

    private static async Task<IResult> GetUnreadCount(
        INotificationRepository notifRepo,
        HttpContext context)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        if (userId == 0) return Results.Unauthorized();

        var count = await notifRepo.GetUnreadCountAsync(userId);

        return Results.Ok(new { count });
    }

    private static async Task<IResult> MarkAsRead(
        long id,
        INotificationRepository notifRepo)
    {
        var result = await notifRepo.MarkAsReadAsync(id);
        if (!result) return Results.NotFound(new { error = "Notification not found." });

        return Results.Ok(new { message = "Notification marked as read." });
    }

    private static async Task<IResult> MarkAllAsRead(
        INotificationRepository notifRepo,
        HttpContext context)
    {
        var userId = (long?)context.Items["UserId"] ?? 0;
        if (userId == 0) return Results.Unauthorized();

        var count = await notifRepo.MarkAllAsReadAsync(userId);

        return Results.Ok(new { message = "All notifications marked as read.", count });
    }

    private static async Task<IResult> DeleteNotification(
        long id,
        INotificationRepository notifRepo)
    {
        var result = await notifRepo.DeleteAsync(id);
        if (!result) return Results.NotFound(new { error = "Notification not found." });

        return Results.Ok(new { message = "Notification deleted successfully." });
    }
}
