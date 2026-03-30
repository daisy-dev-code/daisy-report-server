using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class ReportFolderRepository : IReportFolderRepository
{
    private readonly IDatabase _database;

    public ReportFolderRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<ReportFolder?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ReportFolder>(
            @"SELECT id AS Id, name AS Name, parent_id AS ParentId,
                     description AS Description, created_at AS CreatedAt
              FROM RS_REPORT_FOLDER WHERE id = @Id",
            new { Id = id });
    }

    public async Task<List<ReportFolder>> GetTreeAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        var folders = await conn.QueryAsync<ReportFolder>(
            @"SELECT id AS Id, name AS Name, parent_id AS ParentId,
                     description AS Description, created_at AS CreatedAt
              FROM RS_REPORT_FOLDER
              ORDER BY parent_id ASC, name ASC");
        return folders.ToList();
    }

    public async Task<long> CreateAsync(ReportFolder folder)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_REPORT_FOLDER (name, parent_id, description, created_at)
              VALUES (@Name, @ParentId, @Description, @CreatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                folder.Name,
                folder.ParentId,
                folder.Description,
                folder.CreatedAt
            },
            tx);

        // Insert self-referencing closure entry
        await conn.ExecuteAsync(
            @"INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
              VALUES (@Id, @Id, 0)",
            new { Id = id },
            tx);

        // Insert ancestor closure entries
        if (folder.ParentId.HasValue)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
                  SELECT ancestor_id, @Id, depth + 1
                  FROM RS_REPORT_FOLDER_CLOSURE
                  WHERE descendant_id = @ParentId",
                new { Id = id, ParentId = folder.ParentId.Value },
                tx);
        }

        tx.Commit();
        return id;
    }

    public async Task<bool> UpdateAsync(ReportFolder folder)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_REPORT_FOLDER SET
                name = @Name, parent_id = @ParentId, description = @Description
              WHERE id = @Id",
            new
            {
                folder.Id,
                folder.Name,
                folder.ParentId,
                folder.Description
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        // Delete closure table entries
        await conn.ExecuteAsync(
            @"DELETE FROM RS_REPORT_FOLDER_CLOSURE
              WHERE descendant_id IN (
                  SELECT descendant_id FROM (
                      SELECT descendant_id FROM RS_REPORT_FOLDER_CLOSURE WHERE ancestor_id = @Id
                  ) AS sub
              )",
            new { Id = id },
            tx);

        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_REPORT_FOLDER WHERE id = @Id",
            new { Id = id },
            tx);

        tx.Commit();
        return rows > 0;
    }
}
