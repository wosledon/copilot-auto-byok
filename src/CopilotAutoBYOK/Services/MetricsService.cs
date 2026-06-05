using System.Text;
using copilot_auto_byok.Data;
using copilot_auto_byok.Models.Metrics;
using Microsoft.EntityFrameworkCore;

namespace copilot_auto_byok.Services;

public class MetricsService : IMetricsService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public MetricsService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task RecordAsync(RequestMetrics metrics)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        context.RequestMetrics.Add(new RequestMetricsEntity
        {
            Timestamp = metrics.Timestamp,
            RequestedModel = metrics.RequestedModel,
            ActualModel = metrics.ActualModel,
            Provider = metrics.Provider,
            ProviderId = metrics.ProviderId,
            Protocol = metrics.Protocol,
            IsStreaming = metrics.IsStreaming,
            PromptTokens = metrics.PromptTokens,
            CompletionTokens = metrics.CompletionTokens,
            TotalTokens = metrics.TotalTokens,
            CachedTokens = metrics.CachedTokens,
            LatencyMs = metrics.LatencyMs,
            TotalDurationMs = metrics.TotalDurationMs,
            TokensPerSecond = metrics.TokensPerSecond,
            IsCacheHit = metrics.IsCacheHit,
            StatusCode = metrics.StatusCode,
            IsSuccess = metrics.IsSuccess,
            Error = metrics.Error,
            EstimatedCost = metrics.EstimatedCost
        });
        await context.SaveChangesAsync();
    }

    public async Task<List<RequestMetrics>> GetRequestsAsync(int page, int pageSize, string? model, DateTime? from, DateTime? to)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.RequestMetrics.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(model))
            query = query.Where(r => r.RequestedModel == model || r.ActualModel == model);
        if (from.HasValue)
            query = query.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.Timestamp <= to.Value);

        return await query
            .OrderByDescending(r => r.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => MapToModel(r))
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync(string? model, DateTime? from, DateTime? to)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.RequestMetrics.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(model))
            query = query.Where(r => r.RequestedModel == model || r.ActualModel == model);
        if (from.HasValue)
            query = query.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.Timestamp <= to.Value);

        return await query.CountAsync();
    }

    public async Task<MetricsSummary> GetSummaryAsync(string period)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var from = GetPeriodStart(period);

        var query = context.RequestMetrics.AsNoTracking().Where(r => r.Timestamp >= from);

        var total = await query.CountAsync();
        var success = await query.Where(r => r.IsSuccess).CountAsync();
        var failed = total - success;

        var promptTokens = await query.SumAsync(r => (long)r.PromptTokens);
        var completionTokens = await query.SumAsync(r => (long)r.CompletionTokens);
        var totalTokens = await query.SumAsync(r => (long)r.TotalTokens);
        var cachedTokens = await query.SumAsync(r => (long)r.CachedTokens);
        var estimatedCost = await query.SumAsync(r => r.EstimatedCost);
        var avgLatency = await query.AverageAsync(r => (double?)r.LatencyMs) ?? 0;
        var avgTps = await query.AverageAsync(r => (double?)r.TokensPerSecond) ?? 0;
        var cacheHits = await query.Where(r => r.IsCacheHit).CountAsync();

        var summary = new MetricsSummary
        {
            Period = period,
            TotalRequests = total,
            SuccessfulRequests = success,
            FailedRequests = failed,
            SuccessRate = total > 0 ? Math.Round((double)success / total * 100, 2) : 0,
            TokenUsage = new TokenUsageSummary
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                CachedTokens = cachedTokens,
                EstimatedCost = estimatedCost
            },
            Performance = new PerformanceSummary
            {
                AvgLatencyMs = Math.Round(avgLatency, 2),
                AvgTokensPerSecond = Math.Round(avgTps, 2),
                CacheHitRate = total > 0 ? Math.Round((double)cacheHits / total * 100, 2) : 0
            }
        };

        // Percentiles
        var latencies = await query
            .Where(r => r.LatencyMs > 0)
            .OrderBy(r => r.LatencyMs)
            .Select(r => (double)r.LatencyMs)
            .ToListAsync();

        summary.Performance.P50LatencyMs = GetPercentile(latencies, 0.5);
        summary.Performance.P95LatencyMs = GetPercentile(latencies, 0.95);
        summary.Performance.P99LatencyMs = GetPercentile(latencies, 0.99);

        // Model breakdown
        summary.ModelBreakdown = await query
            .GroupBy(r => r.ActualModel)
            .Select(g => new ModelBreakdown
            {
                Model = g.Key,
                Requests = g.Count(),
                Tokens = g.Sum(r => (long)r.TotalTokens),
                AvgLatencyMs = Math.Round(g.Average(r => (double)r.LatencyMs), 2),
                EstimatedCost = g.Sum(r => r.EstimatedCost)
            })
            .OrderByDescending(m => m.Requests)
            .ToListAsync();

        return summary;
    }

    public async Task<Dictionary<string, object>> GetRealtimeStatsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);

        var query = context.RequestMetrics.AsNoTracking().Where(r => r.Timestamp >= oneHourAgo);

        var requests = await query.CountAsync();
        var tokens = await query.SumAsync(r => (long?)r.TotalTokens) ?? 0;
        var avgLatency = await query.AverageAsync(r => (double?)r.LatencyMs) ?? 0;

        return new Dictionary<string, object>
        {
            ["requestsLastHour"] = requests,
            ["tokensLastHour"] = tokens,
            ["avgLatencyLastHour"] = Math.Round(avgLatency, 2),
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        };
    }

    public async Task<HourlyMetricsResponse> GetHourlyMetricsAsync(string period)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var from = GetPeriodStart(period);

        var hours = new List<string>();
        var hourData = new Dictionary<string, Dictionary<string, HourlyMetrics>>();

        var now = DateTime.UtcNow;
        var current = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0, DateTimeKind.Utc);
        while (current <= now)
        {
            var hourKey = current.ToString("yyyy-MM-ddTHH");
            hours.Add(hourKey);
            hourData[hourKey] = new Dictionary<string, HourlyMetrics>();
            current = current.AddHours(1);
        }

        // Query all data in period and aggregate in memory (SQLite EF doesn't support
        // string formatting in GroupBy, so raw SQL is avoided here).
        var allData = await context.RequestMetrics
            .AsNoTracking()
            .Where(r => r.Timestamp >= from)
            .Select(r => new { r.Timestamp, r.ActualModel, r.Provider, r.TotalTokens, r.LatencyMs, r.TokensPerSecond, r.EstimatedCost })
            .ToListAsync();

        var modelKeys = new HashSet<string>();
        foreach (var r in allData)
        {
            var hourKey = r.Timestamp.ToString("yyyy-MM-ddTHH");
            var model = r.ActualModel;
            var provider = r.Provider;
            var key = $"{provider}/{model}";
            modelKeys.Add(key);

            if (!hourData.ContainsKey(hourKey)) continue;

            if (!hourData[hourKey].ContainsKey(key))
            {
                hourData[hourKey][key] = new HourlyMetrics
                {
                    Hour = hourKey,
                    Requests = 0,
                    Tokens = 0,
                    AvgLatencyMs = 0,
                    AvgTokensPerSecond = 0,
                    EstimatedCost = 0
                };
            }

            var hm = hourData[hourKey][key];
            hm.Requests++;
            hm.Tokens += r.TotalTokens;
            hm.EstimatedCost += r.EstimatedCost;
            // Running average for latency and TPS
            hm.AvgLatencyMs = (hm.AvgLatencyMs * (hm.Requests - 1) + r.LatencyMs) / hm.Requests;
            hm.AvgTokensPerSecond = (hm.AvgTokensPerSecond * (hm.Requests - 1) + r.TokensPerSecond) / hm.Requests;
        }

        var series = new List<HourlyModelSeries>();
        foreach (var key in modelKeys.OrderBy(k => k))
        {
            var parts = key.Split('/', 2);
            var s = new HourlyModelSeries
            {
                Model = parts.Length > 1 ? parts[1] : key,
                Provider = parts.Length > 1 ? parts[0] : ""
            };

            foreach (var h in hours)
            {
                if (hourData[h].TryGetValue(key, out var m))
                {
                    s.Requests.Add(m.Requests);
                    s.Tokens.Add(m.Tokens);
                    s.Latency.Add(Math.Round(m.AvgLatencyMs, 2));
                    s.Tps.Add(Math.Round(m.AvgTokensPerSecond, 2));
                }
                else
                {
                    s.Requests.Add(0);
                    s.Tokens.Add(0);
                    s.Latency.Add(0);
                    s.Tps.Add(0);
                }
            }
            series.Add(s);
        }

        return new HourlyMetricsResponse
        {
            Hours = hours.Select(h => h.Substring(11, 2) + ":00").ToList(),
            Series = series
        };
    }

    public async Task<List<string>> GetDistinctModelsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.RequestMetrics
            .AsNoTracking()
            .Where(r => r.ActualModel != null)
            .Select(r => r.ActualModel)
            .Distinct()
            .OrderBy(m => m)
            .ToListAsync();
    }

    public async Task<string> ExportCsvAsync(DateTime? from, DateTime? to)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var query = context.RequestMetrics.AsNoTracking().AsQueryable();

        if (from.HasValue)
            query = query.Where(r => r.Timestamp >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.Timestamp <= to.Value);

        var data = await query
            .OrderByDescending(r => r.Timestamp)
            .Select(r => MapToModel(r))
            .ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("Timestamp,RequestedModel,ActualModel,Provider,ProviderId,Protocol,IsStreaming,PromptTokens,CompletionTokens,TotalTokens,CachedTokens,LatencyMs,TotalDurationMs,TokensPerSecond,IsCacheHit,StatusCode,IsSuccess,Error,EstimatedCost");

        foreach (var m in data)
        {
            csv.AppendLine($"{m.Timestamp:O},{m.RequestedModel},{m.ActualModel},{m.Provider},{m.ProviderId},{m.Protocol},{m.IsStreaming},{m.PromptTokens},{m.CompletionTokens},{m.TotalTokens},{m.CachedTokens},{m.LatencyMs},{m.TotalDurationMs},{m.TokensPerSecond},{m.IsCacheHit},{m.StatusCode},{m.IsSuccess},\"{m.Error?.Replace("\"", "\"\"")}\",{m.EstimatedCost}");
        }

        return csv.ToString();
    }

    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return Math.Round(sortedValues[index], 2);
    }

    private static DateTime GetPeriodStart(string period)
    {
        return period.ToLower() switch
        {
            "1h" => DateTime.UtcNow.AddHours(-1),
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddHours(-24)
        };
    }

    private static RequestMetrics MapToModel(RequestMetricsEntity e) => new()
    {
        Id = e.Id,
        Timestamp = e.Timestamp,
        RequestedModel = e.RequestedModel,
        ActualModel = e.ActualModel,
        Provider = e.Provider,
        ProviderId = e.ProviderId,
        Protocol = e.Protocol,
        IsStreaming = e.IsStreaming,
        PromptTokens = e.PromptTokens,
        CompletionTokens = e.CompletionTokens,
        TotalTokens = e.TotalTokens,
        CachedTokens = e.CachedTokens,
        LatencyMs = e.LatencyMs,
        TotalDurationMs = e.TotalDurationMs,
        TokensPerSecond = e.TokensPerSecond,
        IsCacheHit = e.IsCacheHit,
        StatusCode = e.StatusCode,
        IsSuccess = e.IsSuccess,
        Error = e.Error,
        EstimatedCost = e.EstimatedCost
    };
}
