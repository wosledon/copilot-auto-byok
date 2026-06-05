using Microsoft.AspNetCore.Mvc;
using copilot_auto_byok.Services;

namespace copilot_auto_byok.Controllers;

[ApiController]
[Route("v1")]
public class AnthropicController : ControllerBase
{
    private readonly IProxyService _proxyService;
    private readonly ILogger<AnthropicController> _logger;

    public AnthropicController(IProxyService proxyService, ILogger<AnthropicController> logger)
    {
        _proxyService = proxyService;
        _logger = logger;
    }

    [HttpPost("messages")]
    public async Task ProxyMessages()
    {
        try
        {
            Request.EnableBuffering();
            var bodyText = await new StreamReader(Request.Body).ReadToEndAsync();
            Request.Body.Position = 0;

            string model = "claude-3-sonnet-20240229";
            bool isStreaming = false;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(bodyText);
                if (doc.RootElement.TryGetProperty("model", out var modelProp))
                    model = modelProp.GetString() ?? "claude-3-sonnet-20240229";
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

            var response = await _proxyService.ForwardAsync(requestMessage, Request.Path + Request.QueryString, bodyText, "anthropic", model, isStreaming);

            Response.StatusCode = (int)response.StatusCode;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

            foreach (var header in response.Headers)
            {
                if (!Response.Headers.ContainsKey(header.Key) &&
                    !header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                    !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

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
            _logger.LogError(ex, "Error in Anthropic proxy");
            if (!Response.HasStarted)
            {
                Response.StatusCode = 500;
                Response.ContentType = "application/json";
                await Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { error = new { message = ex.Message, type = "proxy_error" } }));
            }
        }
    }
}
