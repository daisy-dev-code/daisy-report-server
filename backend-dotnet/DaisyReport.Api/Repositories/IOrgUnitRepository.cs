using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IOrgUnitRepository
{
    Task<OrgUnit?> GetByIdAsync(long id);
    Task<List<OrgUnit>> GetTreeAsync();
    Task<long> CreateAsync(OrgUnit orgUnit);
    Task<bool> UpdateAsync(OrgUnit orgUnit);
    Task<bool> DeleteAsync(long id);
}
