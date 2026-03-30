using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IAuditRepository
{
    Task LogAsync(long? userId, string action, string? entityType, long? entityId, string? details, string? ipAddress);
    Task<(List<AuditLog> Logs, int Total)> ListAsync(int page, int pageSize, long? userId, string? action);
}
