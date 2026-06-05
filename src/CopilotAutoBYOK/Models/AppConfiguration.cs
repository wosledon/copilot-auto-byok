using System.Text.Json.Serialization;

namespace copilot_auto_byok.Models;

public class AppConfiguration
{
    public List<ProviderConfig> Providers { get; set; } = new();
    public AutoCopilotBinding AutoCopilot { get; set; } = new();
    public List<ApiKeyConfig> ApiKeys { get; set; } = new();
    public ByokEnvConfig ByokEnv { get; set; } = new();
}
