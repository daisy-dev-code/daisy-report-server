using System.Text.Json;
using StackExchange.Redis;

namespace DaisyReport.Api.Infrastructure;

public interface IRedisCache
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class;
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task RemoveByPatternAsync(string pattern);
}

public class RedisCache : IRedisCache, IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly StackExchange.Redis.IDatabase _db;
    private readonly ILogger<RedisCache> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisCache(IConfiguration configuration, ILogger<RedisCache> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Redis connection string not configured.");
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            if (expiry.HasValue)
                await _db.StringSetAsync(key, json, new StackExchange.Redis.Expiration(expiry.Value));
            else
                await _db.StringSetAsync(key, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET failed for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DELETE failed for key {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            return await _db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis EXISTS failed for key {Key}", key);
            return false;
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server == null) return;

            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                await _db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis REMOVE_BY_PATTERN failed for pattern {Pattern}", pattern);
        }
    }

    public void Dispose()
    {
        _redis.Dispose();
        GC.SuppressFinalize(this);
    }
}
