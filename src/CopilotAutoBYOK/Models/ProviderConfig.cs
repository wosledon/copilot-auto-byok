namespace copilot_auto_byok.Models;

public class ProviderConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Type { get; set; } = "openai"; // "openai" or "anthropic"
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public List<string> Models { get; set; } = new();
    public List<string> VisibleModels { get; set; } = new();
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
