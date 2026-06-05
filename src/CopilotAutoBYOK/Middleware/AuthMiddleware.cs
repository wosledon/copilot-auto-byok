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
        _logger.LogInformation("AuthMiddleware processing: {Path}", context.Request.Path);

        // Skip auth for static files and non-proxy/admin endpoints
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (path.StartsWith("/index.html") ||
            path.StartsWith("/css/") ||
            path.StartsWith("/js/") ||
            path.StartsWith("/favicon"))
        {
            await _next(context);
            return;
        }

        // Check if API keys are configured
        _logger.LogInformation("Getting API keys...");
        var apiKeys = configService.GetApiKeys();
        _logger.LogInformation("API keys count: {Count}", apiKeys.Count);
        if (apiKeys.Count == 0)
        {
            // No keys configured, allow all requests (setup mode)
            _logger.LogInformation("No API keys, allowing request");
            await _next(context);
            return;
        }

        // Extract API key from Authorization header
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
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
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":{\"message\":\"Invalid API key\",\"type\":\"auth_error\"}}");
            return;
        }

        await _next(context);
    }
}
