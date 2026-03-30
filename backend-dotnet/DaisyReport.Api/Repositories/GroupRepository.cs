using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class GroupRepository : IGroupRepository
{
    private readonly IDatabase _database;

    public GroupRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<Group?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Group>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     parent_id AS ParentId, is_active AS IsActive,
                     created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM RS_GROUP WHERE id = @Id",
            new { Id = id });
    }

    public async Task<(List<Group> Groups, int Total)> ListAsync(int page, int pageSize, string? search)
    {
        using var conn = await _database.GetConnectionAsync();

        var whereClause = string.IsNullOrWhiteSpace(search)
            ? ""
            : "WHERE name LIKE @Search OR description LIKE @Search";

        var searchParam = string.IsNullOrWhiteSpace(search) ? null : $"%{search}%";
        var offset = (page - 1) * pageSize;

        var total = await conn.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM RS_GROUP {whereClause}",
            new { Search = searchParam });

        var groups = (await conn.QueryAsync<Group>(
            $@"SELECT id AS Id, name AS Name, description AS Description,
                      parent_id AS ParentId, is_active AS IsActive,
                      created_at AS CreatedAt, updated_at AS UpdatedAt
               FROM RS_GROUP {whereClause}
               ORDER BY id ASC
               LIMIT @PageSize OFFSET @Offset",
            new { Search = searchParam, PageSize = pageSize, Offset = offset })).ToList();

        return (groups, total);
    }

    public async Task<long> CreateAsync(Group group)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_GROUP (name, description, parent_id, is_active, created_at, updated_at)
              VALUES (@Name, @Description, @ParentId, @IsActive, @CreatedAt, @UpdatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                group.Name,
                group.Description,
                group.ParentId,
                group.IsActive,
                group.CreatedAt,
                group.UpdatedAt
            });
        return id;
    }

    public async Task<bool> UpdateAsync(Group group)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_GROUP SET
                name = @Name, description = @Description, parent_id = @ParentId,
                is_active = @IsActive, updated_at = @UpdatedAt
              WHERE id = @Id",
            new
            {
                group.Id,
                group.Name,
                group.Description,
                group.ParentId,
                group.IsActive,
                group.UpdatedAt
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_GROUP WHERE id = @Id",
            new { Id = id });
        return rows > 0;
    }

    public async Task<List<User>> GetMembersAsync(long groupId)
    {
        using var conn = await _database.GetConnectionAsync();
        var members = await conn.QueryAsync<User>(
            @"SELECT u.id AS Id, u.username AS Username, u.email AS Email,
                     u.display_name AS DisplayName, u.role AS Role,
                     u.is_active AS IsActive, u.created_at AS CreatedAt
              FROM RS_USERS u
              INNER JOIN RS_GROUP_MEMBER gm ON gm.user_id = u.id
              WHERE gm.group_id = @GroupId
              ORDER BY u.username ASC",
            new { GroupId = groupId });
        return members.ToList();
    }

    public async Task<bool> AddMemberAsync(long groupId, long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"INSERT IGNORE INTO RS_GROUP_MEMBER (group_id, user_id, created_at)
              VALUES (@GroupId, @UserId, @CreatedAt)",
            new { GroupId = groupId, UserId = userId, CreatedAt = DateTime.UtcNow });
        return rows > 0;
    }

    public async Task<bool> RemoveMemberAsync(long groupId, long userId)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_GROUP_MEMBER WHERE group_id = @GroupId AND user_id = @UserId",
            new { GroupId = groupId, UserId = userId });
        return rows > 0;
    }
}
