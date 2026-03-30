using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IReportRepository
{
    Task<Report?> GetByIdAsync(long id);
    Task<(List<Report> Reports, int Total)> ListAsync(int page, int pageSize, long? folderId);
    Task<long> CreateAsync(Report report);
    Task<bool> UpdateAsync(Report report);
    Task<bool> DeleteAsync(long id);
    Task<List<ReportParameter>> GetParametersAsync(long reportId);
}
