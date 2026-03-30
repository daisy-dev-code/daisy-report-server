using System.Text.RegularExpressions;
using Dapper;
using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.Services;

public class SearchService : ISearchService
{
    private readonly IDatabase _database;
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex NonAlphanumericRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public SearchService(IDatabase database)
    {
        _database = database;
    }

    public async Task<SearchResults> SearchAsync(string query, string? entityType = null, int page = 1, int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchResults { Page = page, PageSize = pageSize };

        var tokens = Tokenize(query);
        if (tokens.Count == 0)
            return new SearchResults { Page = page, PageSize = pageSize };

        using var conn = await _database.GetConnectionAsync();

        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(entityType))
            conditions.Add("entity_type = @EntityType");

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        List<RawSearchRow> rows;

        if (query.Length >= 3)
        {
            // FULLTEXT boolean mode search
            var booleanQuery = string.Join(" ", tokens.Select(t => $"+{t}*"));
            var fulltextWhere = string.IsNullOrEmpty(whereClause)
                ? "WHERE MATCH(field_value) AGAINST(@BooleanQuery IN BOOLEAN MODE)"
                : $"{whereClause} AND MATCH(field_value) AGAINST(@BooleanQuery IN BOOLEAN MODE)";

            rows = (await conn.QueryAsync<RawSearchRow>(
                $@"SELECT entity_type AS EntityType, entity_id AS EntityId, field_name AS FieldName,
                          field_value AS FieldValue,
                          MATCH(field_value) AGAINST(@BooleanQuery IN BOOLEAN MODE) AS Relevance
                   FROM RS_SEARCH_INDEX
                   {fulltextWhere}
                   ORDER BY Relevance DESC
                   LIMIT 500",
                new { BooleanQuery = booleanQuery, EntityType = entityType })).ToList();
        }
        else
        {
            // Short query: LIKE prefix match
            var likePattern = $"{query.ToLowerInvariant()}%";
            var likeWhere = string.IsNullOrEmpty(whereClause)
                ? "WHERE LOWER(field_value) LIKE @LikePattern"
                : $"{whereClause} AND LOWER(field_value) LIKE @LikePattern";

            rows = (await conn.QueryAsync<RawSearchRow>(
                $@"SELECT entity_type AS EntityType, entity_id AS EntityId, field_name AS FieldName,
                          field_value AS FieldValue, 1.0 AS Relevance
                   FROM RS_SEARCH_INDEX
                   {likeWhere}
                   ORDER BY entity_type, entity_id
                   LIMIT 500",
                new { LikePattern = likePattern, EntityType = entityType })).ToList();
        }

        // Score and deduplicate
        var queryLower = query.ToLowerInvariant();
        var grouped = new Dictionary<(string, long), SearchResult>();

        foreach (var row in rows)
        {
            var key = (row.EntityType, row.EntityId);
            var valueLower = (row.FieldValue ?? "").ToLowerInvariant();

            // Field weight: name fields score higher
            double fieldWeight = row.FieldName.Contains("name", StringComparison.OrdinalIgnoreCase) ? 2.0 : 1.0;

            // Match type scoring
            double matchType;
            if (valueLower == queryLower)
                matchType = 3.0; // exact
            else if (valueLower.StartsWith(queryLower))
                matchType = 2.0; // prefix
            else
                matchType = 1.0; // substring

            var score = fieldWeight * matchType * row.Relevance;

            if (!grouped.TryGetValue(key, out var existing) || existing.Score < score)
            {
                var context = row.FieldValue ?? "";
                if (context.Length > 100)
                    context = context[..100] + "...";

                grouped[key] = new SearchResult
                {
                    EntityType = row.EntityType,
                    EntityId = row.EntityId,
                    Name = row.FieldName.Contains("name", StringComparison.OrdinalIgnoreCase)
                        ? (row.FieldValue ?? "") : (existing?.Name ?? ""),
                    MatchField = row.FieldName,
                    MatchContext = context,
                    Score = score
                };
            }
        }

        var allResults = grouped.Values.OrderByDescending(r => r.Score).ToList();
        var totalCount = allResults.Count;
        var offset = (page - 1) * pageSize;
        var paged = allResults.Skip(offset).Take(pageSize).ToList();

        return new SearchResults
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task IndexEntityAsync(string entityType, long entityId, Dictionary<string, string> fields)
    {
        using var conn = await _database.GetConnectionAsync();

        // Remove existing entries for this entity
        await conn.ExecuteAsync(
            "DELETE FROM RS_SEARCH_INDEX WHERE entity_type = @EntityType AND entity_id = @EntityId",
            new { EntityType = entityType, EntityId = entityId });

        // Insert new entries
        foreach (var (fieldName, fieldValue) in fields)
        {
            if (string.IsNullOrWhiteSpace(fieldValue)) continue;

            await conn.ExecuteAsync(
                @"INSERT INTO RS_SEARCH_INDEX (entity_type, entity_id, field_name, field_value, updated_at)
                  VALUES (@EntityType, @EntityId, @FieldName, @FieldValue, @Now)",
                new
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    FieldName = fieldName,
                    FieldValue = fieldValue,
                    Now = DateTime.UtcNow
                });
        }
    }

    public async Task RemoveEntityAsync(string entityType, long entityId)
    {
        using var conn = await _database.GetConnectionAsync();
        await conn.ExecuteAsync(
            "DELETE FROM RS_SEARCH_INDEX WHERE entity_type = @EntityType AND entity_id = @EntityId",
            new { EntityType = entityType, EntityId = entityId });
    }

    public async Task ReindexAllAsync()
    {
        using var conn = await _database.GetConnectionAsync();

        // Clear existing index
        await conn.ExecuteAsync("TRUNCATE TABLE RS_SEARCH_INDEX");

        // Index users
        var users = await conn.QueryAsync(
            "SELECT id, username, email, display_name FROM RS_USERS WHERE is_active = 1");
        foreach (var u in users)
        {
            await IndexEntityAsync("user", (long)u.id, new Dictionary<string, string>
            {
                ["username"] = (string)(u.username ?? ""),
                ["email"] = (string)(u.email ?? ""),
                ["display_name"] = (string)(u.display_name ?? "")
            });
        }

        // Index reports
        var reports = await conn.QueryAsync(
            "SELECT id, name, description FROM RS_REPORTS");
        foreach (var r in reports)
        {
            await IndexEntityAsync("report", (long)r.id, new Dictionary<string, string>
            {
                ["name"] = (string)(r.name ?? ""),
                ["description"] = (string)(r.description ?? "")
            });
        }

        // Index dashboards
        var dashboards = await conn.QueryAsync(
            "SELECT id, name, description FROM RS_DASHBOARDS");
        foreach (var d in dashboards)
        {
            await IndexEntityAsync("dashboard", (long)d.id, new Dictionary<string, string>
            {
                ["name"] = (string)(d.name ?? ""),
                ["description"] = (string)(d.description ?? "")
            });
        }
    }

    private static List<string> Tokenize(string query)
    {
        var cleaned = HtmlTagRegex.Replace(query, " ");
        var lower = cleaned.ToLowerInvariant();
        var parts = NonAlphanumericRegex.Split(lower);
        return parts.Where(p => p.Length >= 3).Distinct().ToList();
    }

    private class RawSearchRow
    {
        public string EntityType { get; set; } = "";
        public long EntityId { get; set; }
        public string FieldName { get; set; } = "";
        public string? FieldValue { get; set; }
        public double Relevance { get; set; }
    }
}
