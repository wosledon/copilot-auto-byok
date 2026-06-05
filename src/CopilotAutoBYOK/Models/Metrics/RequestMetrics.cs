namespace copilot_auto_byok.Models.Metrics;

public class RequestMetrics
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Request info
    public string RequestedModel { get; set; } = "";
    public string ActualModel { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Protocol { get; set; } = "";
    public bool IsStreaming { get; set; }
    
    // Token usage
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int CachedTokens { get; set; }

    // Performance
    public long LatencyMs { get; set; }
    public long TotalDurationMs { get; set; }
    public double TokensPerSecond { get; set; }
    public bool IsCacheHit { get; set; }
    
    // Status
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    
    // Cost
    public double EstimatedCost { get; set; }
}
