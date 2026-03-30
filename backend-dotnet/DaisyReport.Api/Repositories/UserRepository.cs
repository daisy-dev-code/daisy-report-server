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
            @"SELECT id, username, password_hash, email, firstname, lastname,
                     enabled, locked_until, login_failures, password_changed,
                     created_at, updated_at
              FROM RS_USER WHERE id = @Id",
            new { Id = id });
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<User>(
            @"SELECT id, username, password_hash, email, firstname, lastname,
                     enabled, locked_until, login_failures, password_changed,
                     created_at, updated_at
              FROM RS_USER WHERE username = @Username",
            new { Username = username });
    }

    public async Task<long> CreateAsync(User user)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_USER (username, password_hash, email, firstname, lastname, enabled)
              VALUES (@Username, @PasswordHash, @Email, @Firstname, @Lastname, @Enabled);
              SELECT LAST_INSERT_ID();",
            new { user.Username, user.PasswordHash, user.Email, user.Firstname, user.Lastname, user.Enabled });
        return id;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_USER SET email = @Email, firstname = @Firstname, lastname = @Lastname,
                enabled = @Enabled WHERE id = @Id",
            new { user.Id, user.Email, user.Firstname, user.Lastname, user.Enabled });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync("DELETE FROM RS_USER WHERE id = @Id", new { Id = id });
        return rows > 0;
    }

    public async Task<(List<User> Users, int Total)> ListAsync(int page, int pageSize, string? search)
    {
        using var conn = await _database.GetConnectionAsync();

        var where = string.IsNullOrWhiteSpace(search)
            ? "" : "WHERE username LIKE @Search OR email LIKE @Search OR firstname LIKE @Search OR lastname LIKE @Search";
        var searchParam = string.IsNullOrWhiteSpace(search) ? null : $"%{search}%";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM RS_USER {where}", new { Search = searchParam });
        var users = (await conn.QueryAsync<User>(
            $@"SELECT id, username, password_hash, email, firstname, lastname,
                      enabled, locked_until, login_failures, password_changed,
                      created_at, updated_at
               FROM RS_USER {where} ORDER BY id LIMIT @PageSize OFFSET @Offset",
            new { Search = searchParam, PageSize = pageSize, Offset = offset })).ToList();

        return (users, total);
    }

    public async Task UpdateLastLoginAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync("UPDATE RS_USER SET login_failures = 0 WHERE id = @Id", new { Id = id });
    }

    public async Task<bool> UpdatePasswordAsync(long id, string passwordHash)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE RS_USER SET password_hash = @PasswordHash, password_changed = NOW() WHERE id = @Id",
            new { Id = id, PasswordHash = passwordHash });
        return rows > 0;
    }
}
