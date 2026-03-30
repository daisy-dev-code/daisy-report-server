using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IDatasourceRepository
{
    Task<Datasource?> GetByIdAsync(long id);
    Task<List<Datasource>> ListAsync();
    Task<long> CreateAsync(Datasource datasource);
    Task<bool> UpdateAsync(Datasource datasource);
    Task<bool> DeleteAsync(long id);
    Task<(bool Success, string Message)> TestConnectionAsync(long id);
}
