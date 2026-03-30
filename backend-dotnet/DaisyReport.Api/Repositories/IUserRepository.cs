using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(long id);
    Task<User?> GetByUsernameAsync(string username);
    Task<long> CreateAsync(User user);
    Task<bool> UpdateAsync(User user);
    Task<bool> DeleteAsync(long id);
    Task<(List<User> Users, int Total)> ListAsync(int page, int pageSize, string? search);
    Task UpdateLastLoginAsync(long id);
    Task<bool> UpdatePasswordAsync(long id, string passwordHash);
}
