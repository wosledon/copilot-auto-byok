namespace copilot_auto_byok.Models;

public class ByokEnvConfig
{
    // Connection
    public string ProviderBaseUrl { get; set; } = "";
    public string ProviderType { get; set; } = "openai";
    public string ProviderApiKey { get; set; } = "";
    public string ProviderBearerToken { get; set; } = "";
    public string ProviderWireApi { get; set; } = "completions";
    public string ProviderAzureApiVersion { get; set; } = "";

    // Model
    public string Model { get; set; } = "auto-copilot";
    public string ProviderModelId { get; set; } = "";
    public string ProviderWireModel { get; set; } = "";
    public int? ProviderMaxPromptTokens { get; set; }
    public int? ProviderMaxOutputTokens { get; set; }
}
