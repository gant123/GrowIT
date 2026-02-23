using System.Text.Json;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GrowIT.API.Services;

public class ScheduledReportRunnerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledReportRunnerService> _logger;
    private readonly IOptionsMonitor<ReportSchedulerOptions> _options;
    private readonly ReportSchedulerState _state;

    public ScheduledReportRunnerService(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledReportRunnerService> logger,
        IOptionsMonitor<ReportSchedulerOptions> options,
        ReportSchedulerState state)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled report runner started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            _state.Enabled = options.Enabled;

            if (!options.Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            try
            {
                _state.IsRunningCycle = true;
                _state.LastCycleStartedAtUtc = DateTime.UtcNow;
                _state.LastProcessedSchedules = 0;

                var processed = await RunCycleAsync(options, stoppingToken);

                _state.LastProcessedSchedules = processed;
                _state.LastCycleCompletedAtUtc = DateTime.UtcNow;
                _state.LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.LastError = ex.Message;
                _state.LastCycleCompletedAtUtc = DateTime.UtcNow;
                _logger.LogError(ex, "Scheduled report runner cycle failed.");
            }
            finally
            {
                _state.IsRunningCycle = false;
            }

            var delaySeconds = Math.Clamp(_options.CurrentValue.PollSeconds, 10, 3600);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
        }

        _logger.LogInformation("Scheduled report runner stopped.");
    }

    private async Task<int> RunCycleAsync(ReportSchedulerOptions options, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var dueSchedules = await context.ReportSchedules
            .IgnoreQueryFilters()
            .Where(s => s.IsActive && s.NextRun <= now)
            .OrderBy(s => s.NextRun)
            .Take(Math.Clamp(options.MaxSchedulesPerCycle, 1, 200))
            .ToListAsync(cancellationToken);

        if (dueSchedules.Count == 0)
        {
            return 0;
        }

        var processed = 0;
        foreach (var schedule in dueSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runRequestedAt = DateTime.UtcNow;
            var reportType = string.IsNullOrWhiteSpace(schedule.ReportType)
                ? (string.IsNullOrWhiteSpace(options.DefaultReportType) ? "impact-summary" : options.DefaultReportType.Trim())
                : schedule.ReportType.Trim();
            var format = string.IsNullOrWhiteSpace(schedule.Format)
                ? (string.IsNullOrWhiteSpace(options.DefaultFormat) ? "pdf" : options.DefaultFormat.Trim())
                : schedule.Format.Trim().ToLowerInvariant();
            var request = new GenerateReportRequest
            {
                ReportType = reportType,
                Format = format
            };

            var run = new ReportRun
            {
                Id = Guid.NewGuid(),
                TenantId = schedule.TenantId,
                Name = $"{schedule.Name}-{runRequestedAt:yyyyMMdd-HHmm}",
                Format = format,
                ReportType = reportType,
                RequestPayloadJson = JsonSerializer.Serialize(request),
                RequestedByUserId = schedule.CreatedByUserId,
                GeneratedAt = runRequestedAt,
                Status = "Generated",
                CompletedAt = runRequestedAt,
                ErrorMessage = null
            };

            context.ReportRuns.Add(run);
            schedule.NextRun = ComputeNextRun(schedule.Frequency, schedule.NextRun, now);
            schedule.UpdatedAt = now;
            processed++;
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Scheduled report runner processed {Count} schedules.", processed);
        return processed;
    }

    private static DateTime ComputeNextRun(string? frequency, DateTime currentNextRun, DateTime nowUtc)
    {
        var candidate = DateTime.SpecifyKind(currentNextRun, DateTimeKind.Utc);
        var normalized = (frequency ?? "Weekly").Trim().ToLowerInvariant();

        DateTime Next(DateTime dt) => normalized switch
        {
            "daily" => dt.AddDays(1),
            "monthly" => dt.AddMonths(1),
            _ => dt.AddDays(7)
        };

        // Catch up if scheduler was paused/down.
        var safety = 0;
        while (candidate <= nowUtc && safety++ < 366)
        {
            candidate = Next(candidate);
        }

        return candidate;
    }
}
