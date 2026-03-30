using DaisyReport.Api.Services;

namespace DaisyReport.Api.Endpoints;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/search").RequireAuthorization();

        group.MapGet("/", Search);
    }

    private static async Task<IResult> Search(
        ISearchService searchService,
        string? q = null,
        string? type = null,
        int page = 1,
        int pageSize = 25)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest(new { error = "Query parameter 'q' is required." });

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 100) pageSize = 100;

        var results = await searchService.SearchAsync(q, type, page, pageSize);

        return Results.Ok(new
        {
            data = results.Items.Select(r => new
            {
                r.EntityType,
                r.EntityId,
                r.Name,
                r.MatchField,
                r.MatchContext,
                r.Score
            }),
            pagination = new
            {
                page = results.Page,
                pageSize = results.PageSize,
                total = results.TotalCount,
                totalPages = (int)Math.Ceiling((double)results.TotalCount / results.PageSize)
            }
        });
    }
}
