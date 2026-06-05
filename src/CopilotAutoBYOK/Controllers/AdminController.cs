using Microsoft.AspNetCore.Mvc;
using copilot_auto_byok.Services;
using copilot_auto_byok.Models;

namespace copilot_auto_byok.Controllers;

public class FetchModelsRequest
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}

[ApiController]
[Route("api")]
public class AdminController : ControllerBase
{
    private readonly IConfigService _configService;

    public AdminController(IConfigService configService)
    {
        _configService = configService;
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        return Ok(_configService.GetConfiguration());
    }

    [HttpPut("config")]
    public IActionResult UpdateConfig([FromBody] AppConfiguration config)
    {
        _configService.SaveConfiguration(config);
        return Ok(new { message = "Configuration saved" });
    }

    // Provider management
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        return Ok(_configService.GetProviders());
    }

    [HttpPost("providers")]
    public IActionResult AddProvider([FromBody] ProviderConfig provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            return BadRequest(new { error = "API key is required" });
        if (string.IsNullOrWhiteSpace(provider.Name))
            return BadRequest(new { error = "Provider name is required" });

        _configService.AddProvider(provider);
        return Ok(provider);
    }

    [HttpPut("providers/{id}")]
    public IActionResult UpdateProvider(string id, [FromBody] ProviderConfig provider)
    {
        provider.Id = id;
        _configService.UpdateProvider(provider);
        return Ok(new { message = "Provider updated" });
    }

    [HttpDelete("providers/{id}")]
    public IActionResult DeleteProvider(string id)
    {
        _configService.DeleteProvider(id);
        return Ok(new { message = "Provider deleted" });
    }

    [HttpPost("providers/fetch-models")]
    public async Task<IActionResult> FetchModels([FromBody] FetchModelsRequest request)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);

            var baseUrl = request.BaseUrl.TrimEnd('/');

            // Try multiple endpoints that different providers use
            var urlsToTry = new[]
            {
                $"{baseUrl}/models",
                $"{baseUrl}/v1/models",
                baseUrl.Replace("/v1", "") + "/models"
            };

            HttpResponseMessage? response = null;
            string? lastError = null;

            foreach (var url in urlsToTry.Distinct())
            {
                try
                {
                    response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode) break;
                    lastError = await response.Content.ReadAsStringAsync();
                }
                catch { /* try next URL */ }
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                return Ok(new
                {
                    models = new List<string>(),
                    error = $"无法自动获取模型列表。该提供商可能不支持 /models 端点，请手动输入模型名称。{(lastError != null ? $" 最后错误: {lastError}" : "")}"
                });
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);

            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                        models.Add(idProp.GetString() ?? "");
                }
            }
            else if (doc.RootElement.TryGetProperty("models", out var modelsArray) && modelsArray.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in modelsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                        models.Add(idProp.GetString() ?? "");
                }
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                // Some providers return a plain array
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp))
                        models.Add(idProp.GetString() ?? "");
                    else if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                        models.Add(item.GetString() ?? "");
                }
            }

            var result = models.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (result.Count == 0)
            {
                return Ok(new
                {
                    models = new List<string>(),
                    error = "获取到的响应中未找到模型列表，请手动输入模型名称。"
                });
            }

            return Ok(new { models = result });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                models = new List<string>(),
                error = $"获取模型列表失败: {ex.Message}，请手动输入模型名称。"
            });
        }
    }

    [HttpGet("autocopilot")]
    public IActionResult GetAutoCopilot()
    {
        return Ok(_configService.GetAutoCopilotBinding());
    }

    [HttpPut("autocopilot")]
    public IActionResult SetAutoCopilot([FromBody] AutoCopilotBinding binding)
    {
        _configService.UpdateAutoCopilotBinding(binding);
        return Ok(new { message = "AutoCopilot binding updated" });
    }

    [HttpGet("keys")]
    public IActionResult GetApiKeys()
    {
        var keys = _configService.GetApiKeys();
        return Ok(keys.Select(k => new { k.Id, k.Name, k.CreatedAt }));
    }

    [HttpPost("keys")]
    public IActionResult AddApiKey([FromBody] ApiKeyConfig key)
    {
        if (string.IsNullOrWhiteSpace(key.Key))
            return BadRequest(new { error = "API key is required" });

        key.Id = Guid.NewGuid().ToString("N")[..8];
        key.CreatedAt = DateTime.UtcNow;
        _configService.AddApiKey(key);
        return Ok(new { key.Id, key.Name, key.CreatedAt });
    }

    [HttpDelete("keys/{id}")]
    public IActionResult RemoveApiKey(string id)
    {
        _configService.RemoveApiKey(id);
        return Ok(new { message = "API key removed" });
    }

    [HttpGet("byok")]
    public IActionResult GetByokEnv()
    {
        return Ok(_configService.GetByokEnv());
    }

    [HttpPut("byok")]
    public IActionResult UpdateByokEnv([FromBody] ByokEnvConfig config)
    {
        _configService.UpdateByokEnv(config);
        return Ok(new { message = "BYOK environment configuration saved" });
    }
}
