using DaisyReport.Api.Models;

namespace DaisyReport.Api.Services;

public interface IUserService
{
    Task<(User? User, string Token)?> LoginAsync(string username, string password, string? ipAddress, string? userAgent);
    Task LogoutAsync(long userId, string token);
    Task<User?> GetSessionUserAsync(long userId);
    Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword);
    Task<User?> GetByIdAsync(long id);
    Task<long> CreateAsync(User user, string password);
    Task<bool> UpdateAsync(User user);
    Task<bool> DeleteAsync(long id);
    Task<(List<User> Users, int Total)> ListAsync(int page, int pageSize, string? search);
}
