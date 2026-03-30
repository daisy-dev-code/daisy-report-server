using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;
using DaisyReport.Api.Repositories;

namespace DaisyReport.Api.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly IRedisCache _cache;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepo,
        IAuditRepository auditRepo,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        IRedisCache cache,
        ILogger<UserService> logger)
    {
        _userRepo = userRepo;
        _auditRepo = auditRepo;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<(User? User, string Token)?> LoginAsync(string username, string password, string? ipAddress, string? userAgent)
    {
        var user = await _userRepo.GetByUsernameAsync(username);
        if (user == null)
        {
            _logger.LogWarning("Login attempt for non-existent user: {Username}", username);
            return null;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for disabled user: {Username}", username);
            return null;
        }

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Invalid password for user: {Username}", username);
            await _auditRepo.LogAsync(user.Id, "login_failed", "user", user.Id, "Invalid password", ipAddress);
            return null;
        }

        var token = _jwtService.GenerateToken(user);

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _userRepo.UpdateLastLoginAsync(user.Id);

        // Cache the session
        await _cache.SetAsync($"session:{user.Id}", new { UserId = user.Id, Token = token }, TimeSpan.FromHours(1));

        await _auditRepo.LogAsync(user.Id, "login", "user", user.Id, null, ipAddress);
        _logger.LogInformation("User {Username} logged in from {IpAddress}", username, ipAddress);

        return (user, token);
    }

    public async Task LogoutAsync(long userId, string token)
    {
        await _cache.RemoveAsync($"session:{userId}");
        await _auditRepo.LogAsync(userId, "logout", "user", userId, null, null);
        _logger.LogInformation("User {UserId} logged out", userId);
    }

    public async Task<User?> GetSessionUserAsync(long userId)
    {
        return await _userRepo.GetByIdAsync(userId);
    }

    public async Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) return false;

        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            _logger.LogWarning("Change password: invalid current password for user {UserId}", userId);
            return false;
        }

        var newHash = _passwordHasher.HashPassword(newPassword);
        var result = await _userRepo.UpdatePasswordAsync(userId, newHash);

        if (result)
        {
            await _auditRepo.LogAsync(userId, "change_password", "user", userId, null, null);
            _logger.LogInformation("User {UserId} changed password", userId);
        }

        return result;
    }

    public async Task<User?> GetByIdAsync(long id)
    {
        return await _userRepo.GetByIdAsync(id);
    }

    public async Task<long> CreateAsync(User user, string password)
    {
        user.PasswordHash = _passwordHasher.HashPassword(password);
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        var id = await _userRepo.CreateAsync(user);
        await _auditRepo.LogAsync(null, "create_user", "user", id, $"Created user {user.Username}", null);
        return id;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        user.UpdatedAt = DateTime.UtcNow;
        var result = await _userRepo.UpdateAsync(user);
        if (result)
        {
            await _auditRepo.LogAsync(null, "update_user", "user", user.Id, $"Updated user {user.Username}", null);
        }
        return result;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var result = await _userRepo.DeleteAsync(id);
        if (result)
        {
            await _auditRepo.LogAsync(null, "delete_user", "user", id, null, null);
        }
        return result;
    }

    public async Task<(List<User> Users, int Total)> ListAsync(int page, int pageSize, string? search)
    {
        return await _userRepo.ListAsync(page, pageSize, search);
    }
}
