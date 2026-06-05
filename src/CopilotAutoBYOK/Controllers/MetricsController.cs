using Microsoft.AspNetCore.Mvc;
using copilot_auto_byok.Services;

namespace copilot_auto_byok.Controllers;

[ApiController]
[Route("api/metrics")]
public class MetricsController : ControllerBase
{
    private readonly IMetricsService _metricsService;

    public MetricsController(IMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? model = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var requests = await _metricsService.GetRequestsAsync(page, pageSize, model, from, to);
        var total = await _metricsService.GetTotalCountAsync(model, from, to);

        return Ok(new
        {
            data = requests,
            pagination = new
            {
                page,
                pageSize,
                total,
                totalPages = (int)Math.Ceiling((double)total / pageSize)
            }
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] string period = "24h")
    {
        var summary = await _metricsService.GetSummaryAsync(period);
        return Ok(summary);
    }

    [HttpGet("realtime")]
    public async Task<IActionResult> GetRealtime()
    {
        var stats = await _metricsService.GetRealtimeStatsAsync();
        return Ok(stats);
    }

    [HttpGet("hourly")]
    public async Task<IActionResult> GetHourlyMetrics([FromQuery] string period = "24h")
    {
        var data = await _metricsService.GetHourlyMetricsAsync(period);
        return Ok(data);
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetDistinctModels()
    {
        var models = await _metricsService.GetDistinctModelsAsync();
        return Ok(models);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var csv = await _metricsService.ExportCsvAsync(from, to);
        var fileName = $"metrics-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }
}
