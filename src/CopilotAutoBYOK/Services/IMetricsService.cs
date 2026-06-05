using copilot_auto_byok.Models.Metrics;

namespace copilot_auto_byok.Services;

public interface IMetricsService
{
    Task RecordAsync(RequestMetrics metrics);
    Task<List<RequestMetrics>> GetRequestsAsync(int page, int pageSize, string? model, DateTime? from, DateTime? to);
    Task<int> GetTotalCountAsync(string? model, DateTime? from, DateTime? to);
    Task<MetricsSummary> GetSummaryAsync(string period);
    Task<Dictionary<string, object>> GetRealtimeStatsAsync();
    Task<HourlyMetricsResponse> GetHourlyMetricsAsync(string period);
    Task<List<string>> GetDistinctModelsAsync();
    Task<string> ExportCsvAsync(DateTime? from, DateTime? to);
}
