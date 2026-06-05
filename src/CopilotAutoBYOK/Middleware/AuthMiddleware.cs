using copilot_auto_byok.Services;

namespace copilot_auto_byok.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfigService configService)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip auth for static files, admin API, and fallback routes
        if (path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var apiKeys = configService.GetApiKeys();
        if (apiKeys.Count == 0)
        {
            // No keys configured — setup mode, allow all
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":{\"message\":\"Missing or invalid API key. Use Authorization: Bearer <key>\",\"type\":\"auth_error\"}}");
            return;
        }

        var providedKey = authHeader["Bearer ".Length..].Trim();
        var isValid = apiKeys.Any(k => k.Key == providedKey);

        if (!isValid)
        {
            _logger.LogDebug("Invalid API key attempted from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":{\"message\":\"Invalid API key\",\"type\":\"auth_error\"}}");
            return;
        }

        await _next(context);
    }
}
