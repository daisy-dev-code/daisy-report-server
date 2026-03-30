using System.Net.Http.Headers;
using System.Text.Json;
using DaisyReport.Api.Infrastructure;
using Dapper;

namespace DaisyReport.Api.PowerBi.Services;

public interface IPowerBiAuthService
{
    Task<string> GetAccessTokenAsync();
    Task<bool> TestConnectionAsync(string tenantId, string clientId, string clientSecret);
}

public class PowerBiAuthService : IPowerBiAuthService
{
    private readonly IDatabase _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PowerBiAuthService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string Scope = "https://analysis.windows.net/powerbi/api/.default";
    private const int ExpiryBufferSeconds = 300;

    public PowerBiAuthService(
        IDatabase database,
        IHttpClientFactory httpClientFactory,
        ILogger<PowerBiAuthService> logger)
    {
        _database = database;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        await _tokenLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            var (tenantId, clientId, clientSecret) = await LoadConfigFromDbAsync();
            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("Power BI configuration not found. Please configure tenant, client ID, and secret.");

            var token = await RequestTokenAsync(tenantId, clientId, clientSecret);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public async Task<bool> TestConnectionAsync(string tenantId, string clientId, string clientSecret)
    {
        try
        {
            var token = await RequestTokenAsync(tenantId, clientId, clientSecret, cache: false);
            return !string.IsNullOrEmpty(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Power BI connection test failed for tenant {TenantId}", tenantId);
            return false;
        }
    }

    private async Task<string> RequestTokenAsync(string tenantId, string clientId, string clientSecret, bool cache = true)
    {
        var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";

        var client = _httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = Scope
        });

        _logger.LogDebug("Requesting Power BI access token from {TokenUrl}", tokenUrl);

        var response = await client.PostAsync(tokenUrl, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to obtain Power BI token. Status: {Status}, Body: {Body}",
                response.StatusCode, body);
            throw new HttpRequestException($"Failed to obtain Power BI access token: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
        var accessToken = tokenResponse.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("access_token missing from token response");

        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();

        if (cache)
        {
            _cachedToken = accessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - ExpiryBufferSeconds);
        }

        _logger.LogInformation("Power BI access token obtained, expires in {ExpiresIn}s", expiresIn);
        return accessToken;
    }

    private async Task<(string TenantId, string ClientId, string ClientSecret)> LoadConfigFromDbAsync()
    {
        using var conn = await _database.GetConnectionAsync();

        var entries = await conn.QueryAsync<(string ConfigKey, string ConfigValue)>(
            @"SELECT config_key AS ConfigKey, config_value AS ConfigValue
              FROM RS_CONFIG
              WHERE config_key IN ('powerbi.tenant_id', 'powerbi.client_id', 'powerbi.client_secret')");

        var dict = entries.ToDictionary(e => e.ConfigKey, e => e.ConfigValue);

        dict.TryGetValue("powerbi.tenant_id", out var tenantId);
        dict.TryGetValue("powerbi.client_id", out var clientId);
        dict.TryGetValue("powerbi.client_secret", out var clientSecret);

        return (tenantId ?? "", clientId ?? "", clientSecret ?? "");
    }
}
