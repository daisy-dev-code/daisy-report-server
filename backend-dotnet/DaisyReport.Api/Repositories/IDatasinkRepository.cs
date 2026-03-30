using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IDatasinkRepository
{
    Task<Datasink?> GetByIdAsync(long id);
    Task<List<Datasink>> ListAsync();
    Task<long> CreateAsync(Datasink datasink);
    Task<bool> UpdateAsync(Datasink datasink);
    Task<bool> DeleteAsync(long id);
}
