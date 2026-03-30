namespace DaisyReport.Api.Services;

public interface ISearchService
{
    Task<SearchResults> SearchAsync(string query, string? entityType = null, int page = 1, int pageSize = 25);
    Task IndexEntityAsync(string entityType, long entityId, Dictionary<string, string> fields);
    Task RemoveEntityAsync(string entityType, long entityId);
    Task ReindexAllAsync();
}

public class SearchResults
{
    public List<SearchResult> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class SearchResult
{
    public string EntityType { get; set; } = "";
    public long EntityId { get; set; }
    public string Name { get; set; } = "";
    public string MatchField { get; set; } = "";
    public string MatchContext { get; set; } = "";
    public double Score { get; set; }
}
