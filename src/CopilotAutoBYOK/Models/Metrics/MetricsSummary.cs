namespace copilot_auto_byok.Models.Metrics;

public class MetricsSummary
{
    public string Period { get; set; } = "";
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double SuccessRate { get; set; }
    
    public TokenUsageSummary TokenUsage { get; set; } = new();
    public PerformanceSummary Performance { get; set; } = new();
    public List<ModelBreakdown> ModelBreakdown { get; set; } = new();
}

public class TokenUsageSummary
{
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long TotalTokens { get; set; }
    public long CachedTokens { get; set; }
    public double EstimatedCost { get; set; }
}

public class PerformanceSummary
{
    public double AvgLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P95LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public double AvgTokensPerSecond { get; set; }
    public double CacheHitRate { get; set; }
}

public class ModelBreakdown
{
    public string Model { get; set; } = "";
    public int Requests { get; set; }
    public long Tokens { get; set; }
    public double AvgLatencyMs { get; set; }
    public double EstimatedCost { get; set; }
}

public class HourlyMetrics
{
    public string Hour { get; set; } = "";
    public int Requests { get; set; }
    public long Tokens { get; set; }
    public double AvgLatencyMs { get; set; }
    public double AvgTokensPerSecond { get; set; }
    public double EstimatedCost { get; set; }
}

public class HourlyMetricsResponse
{
    public List<string> Hours { get; set; } = new();
    public List<HourlyModelSeries> Series { get; set; } = new();
}

public class HourlyModelSeries
{
    public string Model { get; set; } = "";
    public string Provider { get; set; } = "";
    public List<int> Requests { get; set; } = new();
    public List<long> Tokens { get; set; } = new();
    public List<double> Latency { get; set; } = new();
    public List<double> Tps { get; set; } = new();
}
