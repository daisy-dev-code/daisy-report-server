using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface INotificationRepository
{
    Task<(List<Notification> Notifications, int Total)> GetByUserAsync(long userId, bool? unreadOnly, int page, int pageSize);
    Task<int> GetUnreadCountAsync(long userId);
    Task<bool> MarkAsReadAsync(long notificationId);
    Task<int> MarkAllAsReadAsync(long userId);
    Task<long> CreateAsync(Notification notification);
    Task<bool> DeleteAsync(long id);
}
