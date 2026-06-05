using Microsoft.AspNetCore.Mvc;
using copilot_auto_byok.Services;
using copilot_auto_byok.Models;

namespace copilot_auto_byok.Controllers;

[ApiController]
[Route("v1")]
public class OpenAIController : ControllerBase
{
    private readonly IProxyService _proxyService;
    private readonly IConfigService _configService;
    private readonly ILogger<OpenAIController> _logger;

    public OpenAIController(IProxyService proxyService, IConfigService configService, ILogger<OpenAIController> logger)
    {
        _proxyService = proxyService;
        _configService = configService;
        _logger = logger;
    }

    [HttpPost("chat/completions")]
    public async Task ProxyChatCompletions()
    {
        try
        {
            // Extract model from body for metrics
            Request.EnableBuffering();
            var bodyText = await new StreamReader(Request.Body).ReadToEndAsync();
            Request.Body.Position = 0;

            string model = "gpt-3.5-turbo";
            bool isStreaming = false;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(bodyText);
                if (doc.RootElement.TryGetProperty("model", out var modelProp))
                    model = modelProp.GetString() ?? "gpt-3.5-turbo";
                if (doc.RootElement.TryGetProperty("stream", out var streamProp))
                    isStreaming = streamProp.GetBoolean();
            }
            catch { /* ignore parse errors, use defaults */ }

            // Rebuild the original request message for full passthrough
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, Request.Path + Request.QueryString)
            {
                Content = new StringContent(bodyText, System.Text.Encoding.UTF8, "application/json")
            };

            // Copy all headers
            foreach (var header in Request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            var response = await _proxyService.ForwardAsync(requestMessage, Request.Path + Request.QueryString, bodyText, "openai", model, isStreaming);

            // Stream response back
            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

            // Forward response headers (excluding those managed by ASP.NET Core)
            foreach (var header in response.Headers)
            {
                if (!Response.Headers.ContainsKey(header.Key) &&
                    !header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            // For non-streaming, read full content and write it
            if (!isStreaming)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                await Response.WriteAsync(responseContent);
                await Response.Body.FlushAsync();
                return;
            }

            // For streaming (SSE), copy the stream directly without buffering
            Response.Headers.Remove("Content-Length");
            var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await Response.Body.WriteAsync(buffer, 0, read);
                await Response.Body.FlushAsync();
            }
            // Dispose the content to trigger MetricsCollectingStream.Dispose
            response.Content.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OpenAI proxy");
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                Response.ContentType = "application/json";
                await Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { error = new { message = ex.Message, type = "proxy_error" } }));
            }
        }
    }

    [HttpGet("models")]
    public IActionResult GetModels()
    {
        var config = _configService.GetConfiguration();
        var models = new List<Dictionary<string, object>>();

        models.Add(new Dictionary<string, object>
        {
            ["id"] = "auto-copilot",
            ["object"] = "model",
            ["owned_by"] = "autocopilot",
            ["current_model"] = config.AutoCopilot.CurrentModel,
            ["current_provider_id"] = config.AutoCopilot.CurrentProviderId
        });

        foreach (var provider in config.Providers)
        {
            foreach (var modelName in provider.Models)
            {
                models.Add(new Dictionary<string, object>
                {
                    ["id"] = modelName,
                    ["object"] = "model",
                    ["owned_by"] = provider.Name
                });
            }
        }

        return Ok(new { data = models, @object = "list" });
    }
}
