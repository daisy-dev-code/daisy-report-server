using DaisyReport.Api.Infrastructure;

namespace DaisyReport.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/system/health", async (IDatabase database, IRedisCache cache) =>
        {
            var dbOk = false;
            var redisOk = false;

            try
            {
                using var conn = await database.GetConnectionAsync();
                dbOk = conn.State == System.Data.ConnectionState.Open;
            }
            catch { /* db not available */ }

            try
            {
                var testKey = "health:check";
                await cache.SetAsync(testKey, new { ts = DateTime.UtcNow }, TimeSpan.FromSeconds(5));
                redisOk = await cache.ExistsAsync(testKey);
                await cache.RemoveAsync(testKey);
            }
            catch { /* redis not available */ }

            var status = dbOk && redisOk ? "healthy" : "degraded";

            return Results.Ok(new
            {
                status,
                version = "1.0.0",
                timestamp = DateTime.UtcNow,
                services = new
                {
                    mysql = dbOk ? "connected" : "disconnected",
                    redis = redisOk ? "connected" : "disconnected"
                }
            });
        }).AllowAnonymous();
    }
}
