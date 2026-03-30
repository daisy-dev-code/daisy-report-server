using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IConfigRepository
{
    Task<List<ConfigEntry>> ListAsync(string? category = null);
    Task<ConfigEntry?> GetByKeyAsync(string key);
    Task<bool> SetAsync(string key, string value, string? category = null, string? description = null);
    Task<bool> DeleteAsync(string key);
}
