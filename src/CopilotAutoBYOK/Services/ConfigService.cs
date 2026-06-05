using System.Security;
using copilot_auto_byok.Data;
using copilot_auto_byok.Models;
using Microsoft.EntityFrameworkCore;

namespace copilot_auto_byok.Services;

public class ConfigService : IConfigService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly object _autoPilotLock = new();

    public ConfigService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;

        // Ensure database is created
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        // Apply persisted BYOK env on startup in background to avoid blocking
        var byok = context.ByokEnv.OrderBy(e => e.Id).FirstOrDefault();
        if (byok != null && !string.IsNullOrWhiteSpace(byok.ProviderBaseUrl))
        {
            var config = MapToModel(byok);
            Task.Run(() => ApplyByokEnvToUser(config));
        }
    }

    public AppConfiguration GetConfiguration()
    {
        using var context = _contextFactory.CreateDbContext();
        return new AppConfiguration
        {
            Providers = context.Providers.AsNoTracking().Select(p => MapToModel(p)).ToList(),
            AutoCopilot = GetAutoCopilotBinding(),
            ApiKeys = context.ApiKeys.AsNoTracking().Select(k => new ApiKeyConfig
            {
                Id = k.Id,
                Key = k.Key,
                Name = k.Name,
                CreatedAt = k.CreatedAt
            }).ToList(),
            ByokEnv = GetByokEnv()
        };
    }

    public void SaveConfiguration(AppConfiguration config)
    {
        // Not used with EF Core — individual updates are preferred
    }

    public List<ProviderConfig> GetProviders()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.Providers.AsNoTracking().Select(p => MapToModel(p)).ToList();
    }

    public ProviderConfig? GetProvider(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.Providers.AsNoTracking().FirstOrDefault(p => p.Id == id);
        return entity == null ? null : MapToModel(entity);
    }

    public void AddProvider(ProviderConfig provider)
    {
        using var context = _contextFactory.CreateDbContext();
        if (string.IsNullOrEmpty(provider.Id))
            provider.Id = Guid.NewGuid().ToString("N");
        if (provider.CreatedAt == default)
            provider.CreatedAt = DateTime.UtcNow;

        context.Providers.Add(MapToEntity(provider));
        context.SaveChanges();
    }

    public void UpdateProvider(ProviderConfig provider)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.Providers.FirstOrDefault(p => p.Id == provider.Id);
        if (entity == null) return;

        entity.Name = provider.Name;
        entity.Type = provider.Type;
        entity.BaseUrl = provider.BaseUrl;
        entity.ApiKey = provider.ApiKey;
        entity.SetModels(provider.Models);
        entity.SetVisibleModels(provider.VisibleModels);
        entity.Description = provider.Description;
        context.SaveChanges();
    }

    public void DeleteProvider(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.Providers.FirstOrDefault(p => p.Id == id);
        if (entity == null) return;
        context.Providers.Remove(entity);
        context.SaveChanges();
    }

    public AutoCopilotBinding GetAutoCopilotBinding()
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.AutoCopilot.OrderBy(e => e.Id).FirstOrDefault();
        if (entity != null)
        {
            return new AutoCopilotBinding
            {
                CurrentModel = entity.CurrentModel,
                CurrentProviderId = entity.CurrentProviderId
            };
        }

        // Entity not yet seeded — create it (under lock to avoid duplicate rows)
        lock (_autoPilotLock)
        {
            entity = context.AutoCopilot.OrderBy(e => e.Id).FirstOrDefault();
            if (entity != null)
            {
                return new AutoCopilotBinding
                {
                    CurrentModel = entity.CurrentModel,
                    CurrentProviderId = entity.CurrentProviderId
                };
            }

            entity = new AutoCopilotBindingEntity();
            context.AutoCopilot.Add(entity);
            context.SaveChanges();
            return new AutoCopilotBinding
            {
                CurrentModel = entity.CurrentModel,
                CurrentProviderId = entity.CurrentProviderId
            };
        }
    }

    public void UpdateAutoCopilotBinding(AutoCopilotBinding binding)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.AutoCopilot.OrderBy(e => e.Id).FirstOrDefault();
        if (entity == null)
        {
            entity = new AutoCopilotBindingEntity();
            context.AutoCopilot.Add(entity);
        }
        entity.CurrentModel = binding.CurrentModel;
        entity.CurrentProviderId = binding.CurrentProviderId;
        context.SaveChanges();
    }

    public List<ApiKeyConfig> GetApiKeys()
    {
        using var context = _contextFactory.CreateDbContext();
        return context.ApiKeys.AsNoTracking().Select(k => new ApiKeyConfig
        {
            Id = k.Id,
            Key = k.Key,
            Name = k.Name,
            CreatedAt = k.CreatedAt
        }).ToList();
    }

    public void AddApiKey(ApiKeyConfig key)
    {
        using var context = _contextFactory.CreateDbContext();
        context.ApiKeys.Add(new ApiKeyConfigEntity
        {
            Id = key.Id,
            Key = key.Key,
            Name = key.Name,
            CreatedAt = key.CreatedAt
        });
        context.SaveChanges();
    }

    public void RemoveApiKey(string id)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.ApiKeys.FirstOrDefault(k => k.Id == id);
        if (entity == null) return;
        context.ApiKeys.Remove(entity);
        context.SaveChanges();
    }

    public ByokEnvConfig GetByokEnv()
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.ByokEnv.OrderBy(e => e.Id).FirstOrDefault();
        return entity == null ? new ByokEnvConfig() : MapToModel(entity);
    }

    public void UpdateByokEnv(ByokEnvConfig config)
    {
        using var context = _contextFactory.CreateDbContext();
        var entity = context.ByokEnv.OrderBy(e => e.Id).FirstOrDefault();
        if (entity == null)
        {
            entity = new ByokEnvConfigEntity();
            context.ByokEnv.Add(entity);
        }
        entity.ProviderBaseUrl = config.ProviderBaseUrl;
        entity.ProviderType = config.ProviderType;
        entity.ProviderApiKey = config.ProviderApiKey;
        entity.ProviderBearerToken = config.ProviderBearerToken;
        entity.ProviderWireApi = config.ProviderWireApi;
        entity.ProviderAzureApiVersion = config.ProviderAzureApiVersion;
        entity.Model = config.Model;
        entity.ProviderModelId = config.ProviderModelId;
        entity.ProviderWireModel = config.ProviderWireModel;
        entity.ProviderMaxPromptTokens = config.ProviderMaxPromptTokens;
        entity.ProviderMaxOutputTokens = config.ProviderMaxOutputTokens;
        context.SaveChanges();

        // Fire-and-forget: setting user env vars touches the Windows registry
        // and can block the request thread, so run it in the background.
        Task.Run(() => ApplyByokEnvToUser(config));
    }

    // ===== Mapping =====
    private static ProviderConfig MapToModel(ProviderConfigEntity e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Type = e.Type,
        ApiKey = e.ApiKey,
        BaseUrl = e.BaseUrl,
        Models = e.GetModels(),
        VisibleModels = e.GetVisibleModels(),
        Description = e.Description,
        CreatedAt = e.CreatedAt
    };

    private static ProviderConfigEntity MapToEntity(ProviderConfig m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Type = m.Type,
        ApiKey = m.ApiKey,
        BaseUrl = m.BaseUrl,
        ModelsJson = System.Text.Json.JsonSerializer.Serialize(m.Models),
        VisibleModelsJson = System.Text.Json.JsonSerializer.Serialize(m.VisibleModels),
        Description = m.Description,
        CreatedAt = m.CreatedAt
    };

    private static ByokEnvConfig MapToModel(ByokEnvConfigEntity e) => new()
    {
        ProviderBaseUrl = e.ProviderBaseUrl,
        ProviderType = e.ProviderType,
        ProviderApiKey = e.ProviderApiKey,
        ProviderBearerToken = e.ProviderBearerToken,
        ProviderWireApi = e.ProviderWireApi,
        ProviderAzureApiVersion = e.ProviderAzureApiVersion,
        Model = e.Model,
        ProviderModelId = e.ProviderModelId,
        ProviderWireModel = e.ProviderWireModel,
        ProviderMaxPromptTokens = e.ProviderMaxPromptTokens,
        ProviderMaxOutputTokens = e.ProviderMaxOutputTokens
    };

    // ===== Environment Variables =====
    private static void ApplyByokEnvToUser(ByokEnvConfig config)
    {
        var target = EnvironmentVariableTarget.User;
        SetUserEnv("COPILOT_PROVIDER_BASE_URL", config.ProviderBaseUrl, target);
        SetUserEnv("COPILOT_PROVIDER_TYPE", config.ProviderType, target);
        SetUserEnv("COPILOT_PROVIDER_API_KEY", config.ProviderApiKey, target);
        SetUserEnv("COPILOT_PROVIDER_BEARER_TOKEN", config.ProviderBearerToken, target);
        SetUserEnv("COPILOT_PROVIDER_WIRE_API", config.ProviderWireApi, target);
        SetUserEnv("COPILOT_PROVIDER_AZURE_API_VERSION", config.ProviderAzureApiVersion, target);
        SetUserEnv("COPILOT_MODEL", config.Model, target);
        SetUserEnv("COPILOT_PROVIDER_MODEL_ID", config.ProviderModelId, target);
        SetUserEnv("COPILOT_PROVIDER_WIRE_MODEL", config.ProviderWireModel, target);
        SetUserEnv("COPILOT_PROVIDER_MAX_PROMPT_TOKENS", config.ProviderMaxPromptTokens?.ToString(), target);
        SetUserEnv("COPILOT_PROVIDER_MAX_OUTPUT_TOKENS", config.ProviderMaxOutputTokens?.ToString(), target);
    }

    private static void SetUserEnv(string name, string? value, EnvironmentVariableTarget target)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value))
                Environment.SetEnvironmentVariable(name, value, target);
            else
                Environment.SetEnvironmentVariable(name, null, target);
        }
        catch (SecurityException)
        {
            // Insufficient privileges — silently skip
        }
    }
}
