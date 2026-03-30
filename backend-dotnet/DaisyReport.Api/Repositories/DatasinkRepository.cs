using Dapper;
using DaisyReport.Api.Infrastructure;
using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public class DatasinkRepository : IDatasinkRepository
{
    private readonly IDatabase _database;

    public DatasinkRepository(IDatabase database)
    {
        _database = database;
    }

    public async Task<Datasink?> GetByIdAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Datasink>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     dtype AS Dtype, folder_id AS FolderId, created_at AS CreatedAt
              FROM RS_DATASINK WHERE id = @Id",
            new { Id = id });
    }

    public async Task<List<Datasink>> ListAsync()
    {
        using var conn = await _database.GetConnectionAsync();
        var datasinks = await conn.QueryAsync<Datasink>(
            @"SELECT id AS Id, name AS Name, description AS Description,
                     dtype AS Dtype, folder_id AS FolderId, created_at AS CreatedAt
              FROM RS_DATASINK
              ORDER BY id ASC");
        return datasinks.ToList();
    }

    public async Task<long> CreateAsync(Datasink datasink)
    {
        using var conn = await _database.GetConnectionAsync();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO RS_DATASINK (name, description, dtype, folder_id, created_at)
              VALUES (@Name, @Description, @Dtype, @FolderId, @CreatedAt);
              SELECT LAST_INSERT_ID();",
            new
            {
                datasink.Name,
                datasink.Description,
                datasink.Dtype,
                datasink.FolderId,
                datasink.CreatedAt
            });
        return id;
    }

    public async Task<bool> UpdateAsync(Datasink datasink)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            @"UPDATE RS_DATASINK SET
                name = @Name, description = @Description, dtype = @Dtype,
                folder_id = @FolderId
              WHERE id = @Id",
            new
            {
                datasink.Id,
                datasink.Name,
                datasink.Description,
                datasink.Dtype,
                datasink.FolderId
            });
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        using var conn = await _database.GetConnectionAsync();
        var rows = await conn.ExecuteAsync(
            "DELETE FROM RS_DATASINK WHERE id = @Id",
            new { Id = id });
        return rows > 0;
    }
}
