using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDatabase _database;

    public UserRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<User?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            @"SELECT id AS Id, username AS Username, password_hash AS PasswordHash,
                     email AS Email, display_name AS DisplayName, group_id AS GroupId,
                     org_unit_id AS OrgUnitId, role AS Role, is_active AS IsActive,
                     must_change_password AS MustChangePassword, last_login_at AS LastLoginAt,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_USERS WHERE id = @Id",
            new { Id = id });
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            @"SELECT id AS Id, username AS Username, password_hash AS PasswordHash,
                     email AS Email, display_name AS DisplayName, group_id AS GroupId,
                     org_unit_id AS OrgUnitId, role AS Role, is_active AS IsActive,
                     must_change_password AS MustChangePassword, last_login_at AS LastLoginAt,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_USERS WHERE username = @Username",
            new { Username = username });
    }

    public async Task<long> CreateAsync(User user)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_USERS (username, password_hash, email, display_name, group_id, org_unit_id, role, is_active, must_change_password, created_at, updated_at)
              VALUES (@Username, @PasswordHash, @Email, @DisplayName, @GroupId, @OrgUnitId, @Role, @IsActive, @MustChangePassword, @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                user.Username,
                user.PasswordHash,
                user.Email,
                user.DisplayName,
                user.GroupId,
                user.OrgUnitId,
                user.Role,
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAt,
                user.UpdatedAt
            });
        return id;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_USERS SET
                username = @Username, email = @Email, display_name = @DisplayName,
                group_id = @GroupId, org_unit_id = @OrgUnitId, role = @Role,
                is_active = @IsActive, must_change_password = @MustChangePassword,
                updated_at = @UpdatedAt
              WHERE id = @Id",
            new
            {
                user.Id,
                user.Username,
                user.Email,
                user.DisplayName,
                user.GroupId,
                user.OrgUnitId,
                user.Role,
                user.IsActive,
                user.MustChangePassword,
                user.UpdatedAt
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE RS_USERS SET is_active = 0, updated_at = @Now WHERE id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
        return rows > 0;
    }

    public async Task<(List<User> Users, int Total)> ListAsync(int page, int pageSize, string? search)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = string.IsNullOrWhiteSpace(search)
            ? ""
            : "WHERE username LIKE @Search OR email LIKE @Search OR display_name LIKE @Search";

        var searchParam = string.IsNullOrWhiteSpace(search) ? null : $"%{search}%";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_USERS {whereClause}",
            new { Search = searchParam });

        var users = (await conn.QueryAsync<User>(
            $@"SELECT id AS Id, username AS Username, password_hash AS PasswordHash,
                      email AS Email, display_name AS DisplayName, group_id AS GroupId,
                      org_unit_id AS OrgUnitId, role AS Role, is_active AS IsActive,
                      must_change_password AS MustChangePassword, last_login_at AS LastLoginAt,
                      created_at AS CreatedAt, updated_at AS UpdatedAt
               FROM RS_USERS {whereClause}
               ORDER BY id ASC
               LIMIT @PageSize OFFSET @Offset",
            new { Search = searchParam, PageSize = pageSize, Offset = offset })).ToList();

        return (users, total);
    }

    public async Task UpdateLastLoginAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE RS_USERS SET last_login_at = @Now WHERE id = @Id",
            new { Id = id, Now = DateTime.UtcNow });
    }

    public async Task<bool> UpdatePasswordAsync(long id, string passwordHash)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE RS_USERS SET password_hash = @PasswordHash, must_change_password = 0, updated_at = @Now WHERE id = @Id",
            new { Id = id, PasswordHash = passwordHash, Now = DateTime.UtcNow });
        return rows > 0;
    }
}
