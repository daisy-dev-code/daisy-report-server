using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IDashboardRepository
{
    Task<Dashboard?> GetByIdAsync(long id);
    Task<(List<Dashboard> Dashboards, int Total)> ListAsync(int page, int pageSize, long? folderId);
    Task<long> CreateAsync(Dashboard dashboard);
    Task<bool> UpdateAsync(Dashboard dashboard);
    Task<bool> DeleteAsync(long id);

    Task<long> AddDadgetAsync(Dadget dadget);
    Task<bool> UpdateDadgetAsync(Dadget dadget);
    Task<bool> RemoveDadgetAsync(long dadgetId);
    Task UpdateLayoutAsync(long dashboardId, List<DadgetPosition> positions);

    Task<List<Dashboard>> GetBookmarksAsync(long userId);
    Task<bool> AddBookmarkAsync(long userId, long dashboardId);
    Task<bool> RemoveBookmarkAsync(long userId, long dashboardId);
}
