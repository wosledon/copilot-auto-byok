using Microsoft.EntityFrameworkCore;
using copilot_auto_byok.Models;

namespace copilot_auto_byok.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ProviderConfigEntity> Providers { get; set; } = null!;
    public DbSet<ApiKeyConfigEntity> ApiKeys { get; set; } = null!;
    public DbSet<AutoCopilotBindingEntity> AutoCopilot { get; set; } = null!;
    public DbSet<ByokEnvConfigEntity> ByokEnv { get; set; } = null!;
    public DbSet<RequestMetricsEntity> RequestMetrics { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderConfigEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ApiKey).IsRequired();
            entity.Property(e => e.BaseUrl).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ModelsJson).HasColumnName("Models");
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<ApiKeyConfigEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Key).IsRequired();
        });

        modelBuilder.Entity<AutoCopilotBindingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CurrentModel).HasMaxLength(200);
            entity.Property(e => e.CurrentProviderId).HasMaxLength(50);
        });

        modelBuilder.Entity<ByokEnvConfigEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProviderBaseUrl).HasMaxLength(500);
            entity.Property(e => e.ProviderType).HasMaxLength(50);
            entity.Property(e => e.ProviderApiKey);
            entity.Property(e => e.ProviderBearerToken);
            entity.Property(e => e.ProviderWireApi).HasMaxLength(50);
            entity.Property(e => e.ProviderAzureApiVersion).HasMaxLength(100);
            entity.Property(e => e.Model).HasMaxLength(200);
            entity.Property(e => e.ProviderModelId).HasMaxLength(200);
            entity.Property(e => e.ProviderWireModel).HasMaxLength(200);
        });

        modelBuilder.Entity<RequestMetricsEntity>(entity =>
        {
            entity.ToTable("request_metrics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.RequestedModel).HasColumnName("requested_model").IsRequired().HasMaxLength(200);
            entity.Property(e => e.ActualModel).HasColumnName("actual_model").IsRequired().HasMaxLength(200);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ProviderId).HasColumnName("provider_id").IsRequired().HasMaxLength(50);
            entity.Property(e => e.Protocol).IsRequired().HasMaxLength(50);
            entity.Property(e => e.IsStreaming).HasColumnName("is_streaming");
            entity.Property(e => e.PromptTokens).HasColumnName("prompt_tokens");
            entity.Property(e => e.CompletionTokens).HasColumnName("completion_tokens");
            entity.Property(e => e.TotalTokens).HasColumnName("total_tokens");
            entity.Property(e => e.CachedTokens).HasColumnName("cached_tokens");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.TotalDurationMs).HasColumnName("total_duration_ms");
            entity.Property(e => e.TokensPerSecond).HasColumnName("tokens_per_second");
            entity.Property(e => e.IsCacheHit).HasColumnName("is_cache_hit");
            entity.Property(e => e.StatusCode).HasColumnName("status_code");
            entity.Property(e => e.IsSuccess).HasColumnName("is_success");
            entity.Property(e => e.EstimatedCost).HasColumnName("estimated_cost");
            entity.Property(e => e.Error).HasMaxLength(1000);
            entity.HasIndex(e => e.Timestamp).HasDatabaseName("idx_timestamp");
            entity.HasIndex(e => e.ActualModel).HasDatabaseName("idx_actual_model");
        });
    }
}

public class ProviderConfigEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Type { get; set; } = "openai";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ModelsJson { get; set; } = "[]";
    public string VisibleModelsJson { get; set; } = "[]";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<string> GetModels() => System.Text.Json.JsonSerializer.Deserialize<List<string>>(ModelsJson) ?? new();
    public void SetModels(List<string> models) => ModelsJson = System.Text.Json.JsonSerializer.Serialize(models);
    public List<string> GetVisibleModels() => System.Text.Json.JsonSerializer.Deserialize<List<string>>(VisibleModelsJson) ?? new();
    public void SetVisibleModels(List<string> models) => VisibleModelsJson = System.Text.Json.JsonSerializer.Serialize(models);
}

public class ApiKeyConfigEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AutoCopilotBindingEntity
{
    public int Id { get; set; } = 1;
    public string CurrentModel { get; set; } = "";
    public string CurrentProviderId { get; set; } = "";
}

public class ByokEnvConfigEntity
{
    public int Id { get; set; } = 1;
    public string ProviderBaseUrl { get; set; } = "";
    public string ProviderType { get; set; } = "openai";
    public string ProviderApiKey { get; set; } = "";
    public string ProviderBearerToken { get; set; } = "";
    public string ProviderWireApi { get; set; } = "completions";
    public string ProviderAzureApiVersion { get; set; } = "";
    public string Model { get; set; } = "auto-copilot";
    public string ProviderModelId { get; set; } = "";
    public string ProviderWireModel { get; set; } = "";
    public int? ProviderMaxPromptTokens { get; set; }
    public int? ProviderMaxOutputTokens { get; set; }
}

public class RequestMetricsEntity
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string RequestedModel { get; set; } = "";
    public string ActualModel { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Protocol { get; set; } = "";
    public bool IsStreaming { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }
    public long LatencyMs { get; set; }
    public long TotalDurationMs { get; set; }
    public double TokensPerSecond { get; set; }
    public bool IsCacheHit { get; set; }
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    public double EstimatedCost { get; set; }
}
