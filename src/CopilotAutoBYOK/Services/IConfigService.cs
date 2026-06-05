using copilot_auto_byok.Models;

namespace copilot_auto_byok.Services;

public interface IConfigService
{
    AppConfiguration GetConfiguration();
    void SaveConfiguration(AppConfiguration config);
    List<ProviderConfig> GetProviders();
    ProviderConfig? GetProvider(string id);
    void AddProvider(ProviderConfig provider);
    void UpdateProvider(ProviderConfig provider);
    void DeleteProvider(string id);
    AutoCopilotBinding GetAutoCopilotBinding();
    void UpdateAutoCopilotBinding(AutoCopilotBinding binding);
    List<ApiKeyConfig> GetApiKeys();
    void AddApiKey(ApiKeyConfig key);
    void RemoveApiKey(string id);
    ByokEnvConfig GetByokEnv();
    void UpdateByokEnv(ByokEnvConfig config);
}
