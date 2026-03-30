using System.Diagnostics;

namespace DaisyReport.Api.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = context.Request.Method;
        var path = context.Request.Path;

        try
        {
            await _next(context);
            sw.Stop();

            var statusCode = context.Response.StatusCode;
            var level = statusCode >= 500 ? LogLevel.Error
                      : statusCode >= 400 ? LogLevel.Warning
                      : LogLevel.Information;

            _logger.Log(level, "{Method} {Path} responded {StatusCode} in {ElapsedMs}ms",
                method, path, statusCode, sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            _logger.LogError("{Method} {Path} threw exception after {ElapsedMs}ms",
                method, path, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
