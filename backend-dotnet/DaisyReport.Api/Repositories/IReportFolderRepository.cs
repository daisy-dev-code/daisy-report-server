using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IReportFolderRepository
{
    Task<ReportFolder?> GetByIdAsync(long id);
    Task<List<ReportFolder>> GetTreeAsync();
    Task<long> CreateAsync(ReportFolder folder);
    Task<bool> UpdateAsync(ReportFolder folder);
    Task<bool> DeleteAsync(long id);
}
