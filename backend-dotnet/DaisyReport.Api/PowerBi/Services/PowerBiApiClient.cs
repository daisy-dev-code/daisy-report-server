using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DaisyReport.Api.PowerBi.Services;

public interface IPowerBiApiClient
{
    Task<T?> GetAsync<T>(string endpoint);
    Task<T?> PostAsync<T>(string endpoint, object? body = null);
    Task DeleteAsync(string endpoint);
    Task<T?> PatchAsync<T>(string endpoint, object body);
    Task<List<T>> GetAllPaginatedAsync<T>(string endpoint, int pageSize = 100);
    Task PostAsync(string endpoint, object? body = null);
}

public class PowerBiApiClient : IPowerBiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IPowerBiAuthService _authService;
    private readonly ILogger<PowerBiApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string BaseUrl = "https://api.powerbi.com/v1.0/myorg/";
    private const int MaxRetries = 3;

    public PowerBiApiClient(
        HttpClient httpClient,
        IPowerBiAuthService authService,
        ILogger<PowerBiApiClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var response = await SendWithRetryAsync(HttpMethod.Get, endpoint);
        var body = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("GET {Endpoint} -> {StatusCode}, Body length: {Length}",
            endpoint, response.StatusCode, body.Length);

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    public async Task<T?> PostAsync<T>(string endpoint, object? body = null)
    {
        var response = await SendWithRetryAsync(HttpMethod.Post, endpoint, body);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("POST {Endpoint} -> {StatusCode}", endpoint, response.StatusCode);

        if (string.IsNullOrWhiteSpace(responseBody))
            return default;

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
    }

    public async Task PostAsync(string endpoint, object? body = null)
    {
        var response = await SendWithRetryAsync(HttpMethod.Post, endpoint, body);
        _logger.LogDebug("POST {Endpoint} -> {StatusCode}", endpoint, response.StatusCode);
    }

    public async Task DeleteAsync(string endpoint)
    {
        var response = await SendWithRetryAsync(HttpMethod.Delete, endpoint);
        _logger.LogDebug("DELETE {Endpoint} -> {StatusCode}", endpoint, response.StatusCode);
    }

    public async Task<T?> PatchAsync<T>(string endpoint, object body)
    {
        var response = await SendWithRetryAsync(HttpMethod.Patch, endpoint, body);
        var responseBody = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("PATCH {Endpoint} -> {StatusCode}", endpoint, response.StatusCode);

        if (string.IsNullOrWhiteSpace(responseBody))
            return default;

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
    }

    public async Task<List<T>> GetAllPaginatedAsync<T>(string endpoint, int pageSize = 100)
    {
        var results = new List<T>();
        var skip = 0;

        while (true)
        {
            var separator = endpoint.Contains('?') ? '&' : '?';
            var pagedEndpoint = $"{endpoint}{separator}$top={pageSize}&$skip={skip}";

            var response = await SendWithRetryAsync(HttpMethod.Get, pagedEndpoint);
            var body = await response.Content.ReadAsStringAsync();

            var wrapper = JsonSerializer.Deserialize<ODataResponse<T>>(body, JsonOptions);
            if (wrapper?.Value == null || wrapper.Value.Count == 0)
                break;

            results.AddRange(wrapper.Value);

            if (wrapper.Value.Count < pageSize)
                break;

            skip += pageSize;
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method, string endpoint, object? body = null)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var token = await _authService.GetAccessTokenAsync();

            var request = new HttpRequestMessage(method, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            _logger.LogDebug("{Method} {Endpoint} (attempt {Attempt})", method, endpoint, attempt + 1);

            var response = await _httpClient.SendAsync(request);

            // Rate limiting — retry with Retry-After
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                _logger.LogWarning("Rate limited on {Endpoint}. Retrying after {Seconds}s",
                    endpoint, retryAfter.TotalSeconds);
                await Task.Delay(retryAfter);
                continue;
            }

            // Server errors — exponential backoff
            if ((int)response.StatusCode >= 500 && attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning("Server error {StatusCode} on {Endpoint}. Retrying in {Seconds}s",
                    response.StatusCode, endpoint, delay.TotalSeconds);
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return response;
        }

        throw new HttpRequestException($"Max retries exceeded for {method} {endpoint}");
    }

    private class ODataResponse<T>
    {
        public List<T> Value { get; set; } = new();
        public string? OdataNextLink { get; set; }
    }
}
