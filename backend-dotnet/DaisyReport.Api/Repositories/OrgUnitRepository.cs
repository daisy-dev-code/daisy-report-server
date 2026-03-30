using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class OrgUnitRepository : IOrgUnitRepository
{
    private readonly IDatabase _database;

    public OrgUnitRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<OrgUnit?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<OrgUnit>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     parent_id AS ParentId, created_at AS CreatedAt
              FROM RS_ORG_UNIT WHERE id = @Id",
            new { Id = id });
    }

    public async Task<List<OrgUnit>> GetTreeAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        var units = await conn.QueryAsync<OrgUnit>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     parent_id AS ParentId, created_at AS CreatedAt
              FROM RS_ORG_UNIT
              ORDER BY parent_id ASC, name ASC");
        return units.ToList();
    }

    public async Task<long> CreateAsync(OrgUnit orgUnit)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_ORG_UNIT (name, description, parent_id, created_at)
              VALUES (@Name, @Description, @ParentId, @CreatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                orgUnit.Name,
                orgUnit.Description,
                orgUnit.ParentId,
                orgUnit.CreatedAt
            },
            tx);

        // Insert self-referencing closure entry
        await conn.ExecuteAsync(
            @"INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
              VALUES (@Id, @Id, 0)",
            new { Id = id },
            tx);

        // Insert ancestor closure entries
        if (orgUnit.ParentId.HasValue)
        {
            await conn.ExecuteAsync(
                @"INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
                  SELECT ancestor_id, @Id, depth + 1
                  FROM RS_ORG_UNIT_CLOSURE
                  WHERE descendant_id = @ParentId",
                new { Id = id, ParentId = orgUnit.ParentId.Value },
                tx);
        }

        tx.Commit();
        return id;
    }

    public async Task<bool> UpdateAsync(OrgUnit orgUnit)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_ORG_UNIT SET
                name = @Name, description = @Description, parent_id = @ParentId
              WHERE id = @Id",
            new
            {
                orgUnit.Id,
                orgUnit.Name,
                orgUnit.Description,
                orgUnit.ParentId
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        using var tx = conn.BeginTransaction();

        // Delete closure table entries where this node is ancestor or descendant
        await conn.ExecuteAsync(
            @"DELETE FROM RS_ORG_UNIT_CLOSURE
              WHERE descendant_id IN (
                  SELECT descendant_id FROM (
                      SELECT descendant_id FROM RS_ORG_UNIT_CLOSURE WHERE ancestor_id = @Id
                  ) AS sub
              )",
            new { Id = id },
            tx);

        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_ORG_UNIT WHERE id = @Id",
            new { Id = id },
            tx);

        tx.Commit();
        return rows > 0;
    }
}
