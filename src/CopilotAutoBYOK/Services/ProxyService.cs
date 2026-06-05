using System.Diagnostics;
using System.Text;
using System.Text.Json;
using copilot_auto_byok.Models;
using copilot_auto_byok.Models.Metrics;

namespace copilot_auto_byok.Services;

public interface IProxyService
{
    Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, string pathAndQuery, string bodyText, string protocol, string requestedModel, bool isStreaming);
}

/// <summary>
/// A stream that copies all read data to a memory buffer for later analysis.
/// When disposed, it parses the collected data for usage metrics.
/// </summary>
internal class MetricsCollectingStream : Stream
{
    private readonly Stream _inner;
    private readonly MemoryStream _copy;
    private readonly string _protocol;
    private readonly RequestMetrics _metrics;
    private readonly Stopwatch _stopwatch;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<ProxyService> _logger;
    private readonly bool _isSuccess;
    private bool _disposed;

    public MetricsCollectingStream(
        Stream inner,
        string protocol,
        RequestMetrics metrics,
        Stopwatch stopwatch,
        IMetricsService metricsService,
        ILogger<ProxyService> logger,
        bool isSuccess)
    {
        _inner = inner;
        _copy = new MemoryStream();
        _protocol = protocol;
        _metrics = metrics;
        _stopwatch = stopwatch;
        _metricsService = metricsService;
        _logger = logger;
        _isSuccess = isSuccess;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
            _copy.Write(buffer, offset, read);
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
        if (read > 0)
            await _copy.WriteAsync(buffer, offset, read, cancellationToken);
        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            try
            {
                _copy.Position = 0;
                var contentText = Encoding.UTF8.GetString(_copy.ToArray());

                if (_protocol == "openai")
                    ParseOpenAIStreamingUsage(contentText, _metrics);
                else
                    ParseAnthropicStreamingUsage(contentText, _metrics);

                _metrics.TotalDurationMs = _stopwatch.ElapsedMilliseconds;
                _metrics.IsSuccess = _isSuccess;
                _metrics.EstimatedCost = CalculateCostStatic(_metrics);
                _metrics.TokensPerSecond = _metrics.TotalDurationMs > 0 && _metrics.CompletionTokens > 0
                    ? Math.Round(_metrics.CompletionTokens / (_metrics.TotalDurationMs / 1000.0), 2)
                    : 0;

                _metricsService.RecordAsync(_metrics).ConfigureAwait(false);
                _logger.LogInformation("Streaming metrics recorded: prompt={Prompt}, completion={Completion}, total={Total}ms, contentLength={ContentLength}",
                    _metrics.PromptTokens, _metrics.CompletionTokens, _metrics.TotalDurationMs, contentText.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming usage");
            }

            _copy.Dispose();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void ParseOpenAIStreamingUsage(string contentText, RequestMetrics metrics)
    {
        try
        {
            var lines = contentText.Split('\n');
            var completionContent = new System.Text.StringBuilder();

            foreach (var line in lines)
            {
                if (line.StartsWith("data: ") && !line.Contains("[DONE]"))
                {
                    var json = line["data: ".Length..];
                    using var doc = JsonDocument.Parse(json);

                    // Try to get usage from chunk
                    if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                        usage.ValueKind == JsonValueKind.Object)
                    {
                        if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.ValueKind == JsonValueKind.Number)
                            metrics.PromptTokens = pt.GetInt32();
                        if (usage.TryGetProperty("completion_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number)
                            metrics.CompletionTokens = ct.GetInt32();
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;

                        if (usage.TryGetProperty("prompt_tokens_details", out var ptd) &&
                            ptd.ValueKind == JsonValueKind.Object &&
                            ptd.TryGetProperty("cached_tokens", out var cached) &&
                            cached.ValueKind == JsonValueKind.Number)
                        {
                            metrics.CachedTokens = cached.GetInt32();
                            metrics.IsCacheHit = metrics.CachedTokens > 0;
                        }
                    }

                    // Collect content for fallback estimation
                    if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                        choices.ValueKind == JsonValueKind.Array &&
                        choices.GetArrayLength() > 0)
                    {
                        var first = choices[0];
                        if (first.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("content", out var content) &&
                                content.ValueKind == JsonValueKind.String)
                            {
                                completionContent.Append(content.GetString());
                            }
                            if (delta.TryGetProperty("reasoning_content", out var reasoning) &&
                                reasoning.ValueKind == JsonValueKind.String)
                            {
                                completionContent.Append(reasoning.GetString());
                            }
                        }
                    }
                }
            }

            // Fallback: estimate tokens from content length if usage not provided
            if (metrics.TotalTokens == 0 && completionContent.Length > 0)
            {
                // Rough estimate: ~4 chars per token for English/Chinese mixed
                var estimatedCompletionTokens = Math.Max(1, completionContent.Length / 4);
                metrics.CompletionTokens = estimatedCompletionTokens;
                metrics.TotalTokens = metrics.PromptTokens + estimatedCompletionTokens;
            }
        }
        catch { }
    }

    private static void ParseAnthropicStreamingUsage(string contentText, RequestMetrics metrics)
    {
        try
        {
            var lines = contentText.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    var json = line["data: ".Length..];
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("type", out var type) &&
                        type.GetString() == "message_delta" &&
                        doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        if (usage.TryGetProperty("output_tokens", out var ot))
                            metrics.CompletionTokens = ot.GetInt32();
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;
                    }
                    if (doc.RootElement.TryGetProperty("type", out var type2) &&
                        type2.GetString() == "message_start" &&
                        doc.RootElement.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("usage", out var usage2))
                    {
                        if (usage2.TryGetProperty("input_tokens", out var it))
                            metrics.PromptTokens = it.GetInt32();
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;
                    }
                }
            }
        }
        catch { }
    }

    private static double CalculateCostStatic(RequestMetrics metrics)
    {
        var pricing = new Dictionary<string, (double input, double output)>
        {
            ["gpt-4o"] = (2.50, 10.00),
            ["gpt-4o-mini"] = (0.15, 0.60),
            ["gpt-3.5-turbo"] = (0.50, 1.50),
            ["gpt-4"] = (30.00, 60.00),
            ["gpt-4-turbo"] = (10.00, 30.00),
            ["claude-3-5-sonnet-20241022"] = (3.00, 15.00),
            ["claude-3-opus-20240229"] = (15.00, 75.00),
            ["claude-3-sonnet-20240229"] = (3.00, 15.00),
            ["claude-3-haiku-20240307"] = (0.25, 1.25),
        };

        var key = metrics.ActualModel.ToLower();
        if (!pricing.TryGetValue(key, out var price))
        {
            foreach (var (k, p) in pricing)
            {
                if (key.Contains(k.ToLower()) || k.ToLower().Contains(key))
                {
                    price = p;
                    break;
                }
            }
        }
        if (price == default) return 0;
        var uncachedPromptTokens = metrics.PromptTokens - metrics.CachedTokens;
        var cachedPromptTokens = metrics.CachedTokens;
        var inputCost = (uncachedPromptTokens / 1_000_000.0) * price.input +
                        (cachedPromptTokens / 1_000_000.0) * price.input * 0.5;
        var outputCost = (metrics.CompletionTokens / 1_000_000.0) * price.output;
        return Math.Round(inputCost + outputCost, 6);
    }
}

public class ProxyService : IProxyService
{
    private readonly IConfigService _configService;
    private readonly IMetricsService _metricsService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProxyService> _logger;

    private static readonly Dictionary<string, (double input, double output)> Pricing = new()
    {
        ["gpt-4o"] = (2.50, 10.00),
        ["gpt-4o-mini"] = (0.15, 0.60),
        ["gpt-3.5-turbo"] = (0.50, 1.50),
        ["gpt-4"] = (30.00, 60.00),
        ["gpt-4-turbo"] = (10.00, 30.00),
        ["claude-3-5-sonnet-20241022"] = (3.00, 15.00),
        ["claude-3-opus-20240229"] = (15.00, 75.00),
        ["claude-3-sonnet-20240229"] = (3.00, 15.00),
        ["claude-3-haiku-20240307"] = (0.25, 1.25),
    };

    public ProxyService(
        IConfigService configService,
        IMetricsService metricsService,
        IHttpClientFactory httpClientFactory,
        ILogger<ProxyService> logger)
    {
        _configService = configService;
        _metricsService = metricsService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, string pathAndQuery, string bodyText, string protocol, string requestedModel, bool isStreaming)
    {
        var config = _configService.GetConfiguration();
        var metrics = new RequestMetrics
        {
            Timestamp = DateTime.UtcNow,
            RequestedModel = requestedModel,
            Protocol = protocol,
            IsStreaming = isStreaming
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Resolve AutoCopilot
            string targetModel = requestedModel;
            string targetProviderId = "";
            string targetProviderType = protocol;

            if (requestedModel.Equals("auto-copilot", StringComparison.OrdinalIgnoreCase))
            {
                targetModel = config.AutoCopilot.CurrentModel;
                targetProviderId = config.AutoCopilot.CurrentProviderId;
                _logger.LogInformation("AutoCopilot resolved: model={TargetModel}, providerId={TargetProviderId}", targetModel, targetProviderId);
            }

            // Resolve provider
            ProviderConfig? provider = null;
            if (!string.IsNullOrEmpty(targetProviderId))
            {
                provider = config.Providers.FirstOrDefault(p => p.Id == targetProviderId);
                if (provider != null) targetProviderType = provider.Type;
            }

            if (provider == null)
            {
                provider = config.Providers.FirstOrDefault(p => p.Type == protocol);
                if (provider == null)
                    throw new InvalidOperationException($"No provider configured for protocol '{protocol}'");
                targetProviderType = provider.Type;
            }

            metrics.Provider = provider.Name;
            metrics.ProviderId = provider.Id;
            metrics.ActualModel = targetModel;

            // Build forward request with full passthrough
            var forwardRequest = await BuildForwardRequestAsync(request, pathAndQuery, bodyText, provider, targetModel, targetProviderType);

            // Send request
            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(forwardRequest, HttpCompletionOption.ResponseHeadersRead);
            metrics.LatencyMs = stopwatch.ElapsedMilliseconds;
            metrics.StatusCode = (int)response.StatusCode;

            // Process response
            if (isStreaming)
            {
                // For streaming, wrap the response stream with MetricsCollectingStream
                // so we can collect usage data while still allowing the caller to read the stream.
                var originalStream = await response.Content.ReadAsStreamAsync();
                var teeStream = new MetricsCollectingStream(
                    originalStream, targetProviderType, metrics, stopwatch, _metricsService, _logger, response.IsSuccessStatusCode);
                // Replace content with the tee stream; preserve original content headers
                var originalHeaders = response.Content.Headers.ToList();
                response.Content = new StreamContent(teeStream);
                foreach (var header in originalHeaders)
                {
                    response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                return response;
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                if (targetProviderType == "openai")
                    ParseOpenAIUsage(responseContent, metrics);
                else
                    ParseAnthropicUsage(responseContent, metrics);
                metrics.TotalDurationMs = stopwatch.ElapsedMilliseconds;
            }

            metrics.IsSuccess = response.IsSuccessStatusCode;
            metrics.EstimatedCost = CalculateCost(metrics);
            metrics.TokensPerSecond = metrics.TotalDurationMs > 0 && metrics.CompletionTokens > 0
                ? Math.Round(metrics.CompletionTokens / (metrics.TotalDurationMs / 1000.0), 2)
                : 0;

            return response;
        }
        catch (Exception ex)
        {
            metrics.Error = ex.Message;
            metrics.IsSuccess = false;
            metrics.StatusCode = 500;
            metrics.TotalDurationMs = stopwatch.ElapsedMilliseconds;
            metrics.EstimatedCost = CalculateCost(metrics);
            _logger.LogError(ex, "Error forwarding request");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            if (metrics.TotalDurationMs == 0)
                metrics.TotalDurationMs = stopwatch.ElapsedMilliseconds;
            // For streaming, metrics are recorded when MetricsCollectingStream is disposed
            if (!isStreaming)
                await _metricsService.RecordAsync(metrics);
        }
    }

    private async Task<HttpRequestMessage> BuildForwardRequestAsync(HttpRequestMessage original, string pathAndQuery, string bodyText, ProviderConfig provider, string model, string providerType)
    {
        // Build target URL preserving path and query
        // Avoid duplicate path segments if base URL already contains /v1
        var targetBase = provider.BaseUrl.TrimEnd('/');
        var path = pathAndQuery;
        if (targetBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring("/v1".Length);
        }
        var targetUrl = $"{targetBase}{path}";

        _logger.LogDebug("Target URL: {TargetUrl} (base={Base}, path={Path})", targetUrl, targetBase, path);

        var forwardRequest = new HttpRequestMessage(original.Method, targetUrl);

        // Forward all headers (except hop-by-hop and auth)
        foreach (var header in original.Headers)
        {
            if (ShouldForwardHeader(header.Key))
            {
                forwardRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Forward content headers
        if (original.Content != null)
        {
            foreach (var header in original.Content.Headers)
            {
                if (!forwardRequest.Content?.Headers.Contains(header.Key) ?? true)
                {
                    // Will be added when content is set
                }
            }
        }

        if (!string.IsNullOrEmpty(bodyText))
        {
            try
            {
                var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(bodyText);
                if (jsonNode is System.Text.Json.Nodes.JsonObject jsonObj)
                {
                    var oldModel = jsonObj["model"]?.ToString() ?? "(none)";
                    jsonObj["model"] = model;
                    var newBody = jsonObj.ToJsonString();
                    _logger.LogInformation("Body replaced: oldModel={OldModel}, newModel={NewModel}, body={Body}", oldModel, model, newBody);
                    forwardRequest.Content = new StringContent(newBody, System.Text.Encoding.UTF8, "application/json");
                }
                else
                {
                    forwardRequest.Content = new StringContent(bodyText, System.Text.Encoding.UTF8, original.Content?.Headers.ContentType?.MediaType ?? "application/json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to parse body as JSON: {Error}. Forwarding as-is.", ex.Message);
                forwardRequest.Content = new StringContent(bodyText, System.Text.Encoding.UTF8, original.Content?.Headers.ContentType?.MediaType ?? "application/json");
            }
        }

        // Override with provider auth
        if (providerType == "openai")
        {
            forwardRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }
        else
        {
            forwardRequest.Headers.TryAddWithoutValidation("x-api-key", provider.ApiKey);
            forwardRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }

        return forwardRequest;
    }

    private static bool ShouldForwardHeader(string headerName)
    {
        var lower = headerName.ToLowerInvariant();
        return lower is not (
            "host" or "connection" or "keep-alive" or "transfer-encoding" or
            "upgrade" or "proxy-authorization" or "proxy-authenticate" or
            "te" or "trailer" or "content-length" or "authorization"
        );
    }

    private async Task ProcessStreamingResponse(HttpResponseMessage response, RequestMetrics metrics, Stopwatch stopwatch, string protocol)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var content = memoryStream.ToArray();
        var contentText = System.Text.Encoding.UTF8.GetString(content);

        if (protocol == "openai")
            ParseOpenAIStreamingUsage(contentText, metrics);
        else
            ParseAnthropicStreamingUsage(contentText, metrics);

        metrics.TotalDurationMs = stopwatch.ElapsedMilliseconds;
        response.Content = new ByteArrayContent(content);
    }

    private void ParseOpenAIUsage(string responseContent, RequestMetrics metrics)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                metrics.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                metrics.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;

                if (usage.TryGetProperty("prompt_tokens_details", out var ptd) &&
                    ptd.TryGetProperty("cached_tokens", out var cached))
                {
                    metrics.CachedTokens = cached.GetInt32();
                    metrics.IsCacheHit = metrics.CachedTokens > 0;
                }
            }
        }
        catch { }
    }

    private void ParseOpenAIStreamingUsage(string contentText, RequestMetrics metrics)
    {
        try
        {
            var lines = contentText.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: ") && !line.Contains("[DONE]"))
                {
                    var json = line["data: ".Length..];
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        metrics.PromptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                        metrics.CompletionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;
                    }
                }
            }
        }
        catch { }
    }

    private void ParseAnthropicUsage(string responseContent, RequestMetrics metrics)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                metrics.PromptTokens = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                metrics.CompletionTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;

                if (usage.TryGetProperty("cache_read_input_tokens", out var cacheRead))
                {
                    metrics.CachedTokens = cacheRead.GetInt32();
                    metrics.IsCacheHit = metrics.CachedTokens > 0;
                }
            }
        }
        catch { }
    }

    private void ParseAnthropicStreamingUsage(string contentText, RequestMetrics metrics)
    {
        try
        {
            var lines = contentText.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    var json = line["data: ".Length..];
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("type", out var type) &&
                        type.GetString() == "message_delta" &&
                        doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        metrics.CompletionTokens = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;

                        if (usage.TryGetProperty("cache_read_input_tokens", out var cacheRead))
                        {
                            metrics.CachedTokens = cacheRead.GetInt32();
                            metrics.IsCacheHit = metrics.CachedTokens > 0;
                        }
                    }
                    if (doc.RootElement.TryGetProperty("type", out var type2) &&
                        type2.GetString() == "message_start" &&
                        doc.RootElement.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("usage", out var usage2))
                    {
                        metrics.PromptTokens = usage2.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                        metrics.TotalTokens = metrics.PromptTokens + metrics.CompletionTokens;
                    }
                }
            }
        }
        catch { }
    }

    private double CalculateCost(RequestMetrics metrics)
    {
        var key = metrics.ActualModel.ToLower();
        if (!Pricing.TryGetValue(key, out var price))
        {
            foreach (var (k, p) in Pricing)
            {
                if (key.Contains(k.ToLower()) || k.ToLower().Contains(key))
                {
                    price = p;
                    break;
                }
            }
        }

        if (price == default)
            return 0;

        // Cache hit tokens are discounted 50% for prompt/input tokens
        var uncachedPromptTokens = metrics.PromptTokens - metrics.CachedTokens;
        var cachedPromptTokens = metrics.CachedTokens;

        var inputCost = (uncachedPromptTokens / 1_000_000.0) * price.input +
                        (cachedPromptTokens / 1_000_000.0) * price.input * 0.5;
        var outputCost = (metrics.CompletionTokens / 1_000_000.0) * price.output;
        return Math.Round(inputCost + outputCost, 6);
    }
}
