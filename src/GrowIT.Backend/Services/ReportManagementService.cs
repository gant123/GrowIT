using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using InvestmentStatus = GrowIT.Shared.Enums.InvestmentStatus;
using ImpactOutcome = GrowIT.Shared.Enums.ImpactOutcome;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrowIT.Backend.Services;

public interface IReportManagementService
{
    Task<List<RecentReport>> GetRecentReportsAsync(RecentReportsQueryParams query, CancellationToken cancellationToken);
    Task<List<ScheduledReport>> GetScheduledReportsAsync(ScheduledReportsQueryParams query, CancellationToken cancellationToken);
    Task<RecentReport> GenerateAsync(GenerateReportRequest request, CancellationToken cancellationToken);
    Task<ReportManagementService.ReportDownloadFile> DownloadAsync(Guid id, string? clientIp, string? userAgent, CancellationToken cancellationToken);
    Task<ReportRunDetailDto?> GetReportRunDetailAsync(Guid id, CancellationToken cancellationToken);
    Task<ScheduledReport> CreateScheduledAsync(CreateScheduledReportRequest request, CancellationToken cancellationToken);
    Task<ScheduledReport?> UpdateScheduledAsync(Guid id, UpdateScheduledReportRequest request, CancellationToken cancellationToken);
    Task DeleteScheduledAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ReportManagementService : IReportManagementService
{
    private static readonly TimeSpan DuplicateReportWindow = TimeSpan.FromMinutes(2);

    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public ReportManagementService(ApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<List<RecentReport>> GetRecentReportsAsync(RecentReportsQueryParams query, CancellationToken cancellationToken)
    {
        var runs = _context.ReportRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            runs = runs.Where(r => r.Name.Contains(search) || r.ReportType.Contains(search) || r.Format.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.ReportType))
        {
            var reportType = NormalizeReportType(query.ReportType, allowCustom: true);
            runs = runs.Where(r => r.ReportType == reportType);
        }

        if (!string.IsNullOrWhiteSpace(query.Format))
        {
            var format = NormalizeReportFormat(query.Format);
            runs = runs.Where(r => r.Format == format);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = NormalizeReportStatus(query.Status);
            runs = runs.Where(r => r.Status == status || (status == "Generated" && string.IsNullOrWhiteSpace(r.Status)));
        }

        if (query.DateFrom.HasValue)
        {
            var from = NormalizeUtc(query.DateFrom.Value);
            runs = runs.Where(r => r.GeneratedAt >= from);
        }

        if (query.DateTo.HasValue)
        {
            var inclusiveEnd = NormalizeUtc(query.DateTo.Value.Date.AddDays(1).AddTicks(-1));
            runs = runs.Where(r => r.GeneratedAt <= inclusiveEnd);
        }

        var take = Math.Clamp(query.Take ?? 200, 1, 1000);

        return await runs
            .OrderByDescending(r => r.GeneratedAt)
            .Take(take)
            .Select(r => new RecentReport
            {
                Id = r.Id,
                Name = r.Name,
                Format = r.Format,
                ReportType = r.ReportType,
                Status = string.IsNullOrWhiteSpace(r.Status) ? "Generated" : r.Status,
                ErrorMessage = r.ErrorMessage,
                CompletedAt = r.CompletedAt,
                LastDownloadedAt = r.LastDownloadedAt,
                GeneratedAt = r.GeneratedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ScheduledReport>> GetScheduledReportsAsync(ScheduledReportsQueryParams query, CancellationToken cancellationToken)
    {
        var schedules = _context.ReportSchedules.AsNoTracking().AsQueryable();

        if (!query.IncludeInactive)
        {
            schedules = schedules.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            schedules = schedules.Where(s => s.Name.Contains(search) || s.Frequency.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Frequency))
        {
            var frequency = NormalizeFrequency(query.Frequency);
            schedules = schedules.Where(s => s.Frequency == frequency);
        }

        var take = Math.Clamp(query.Take ?? 200, 1, 1000);

        return await schedules
            .OrderBy(s => s.NextRun)
            .Take(take)
            .Select(s => new ScheduledReport
            {
                Id = s.Id,
                Name = s.Name,
                ReportType = s.ReportType,
                Format = s.Format,
                Frequency = s.Frequency,
                NextRun = s.NextRun,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<RecentReport> GenerateAsync(GenerateReportRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("No valid tenant context found.");
        }

        var reportType = NormalizeReportType(request.ReportType, allowCustom: true);
        var format = NormalizeReportFormat(request.Format);
        var normalizedRequest = NormalizeGenerateRequest(request, reportType, format);
        var now = DateTime.UtcNow;
        var requestPayloadJson = JsonSerializer.Serialize(normalizedRequest);
        var requestFingerprint = Sha256Hex(requestPayloadJson);
        var requestedByUserId = _currentUserService.UserId;
        var recentSince = now.Subtract(DuplicateReportWindow);

        var existing = await _context.ReportRuns
            .AsNoTracking()
            .Where(r =>
                r.TenantId == tenantId.Value &&
                r.RequestedByUserId == requestedByUserId &&
                r.RequestFingerprint == requestFingerprint &&
                r.GeneratedAt >= recentSince &&
                r.Status != "Failed")
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return MapRecentReport(existing);
        }

        var run = new ReportRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            Name = BuildReportName(reportType, normalizedRequest.TimeZone, now),
            Format = format,
            ReportType = reportType,
            RequestPayloadJson = requestPayloadJson,
            RequestFingerprint = requestFingerprint,
            IdempotencyKey = BuildIdempotencyKey("report", tenantId.Value, requestedByUserId, requestFingerprint, now, DuplicateReportWindow),
            Status = "Queued",
            RequestedByUserId = requestedByUserId,
            GeneratedAt = now
        };

        _context.ReportRuns.Add(run);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicate = await _context.ReportRuns
                .AsNoTracking()
                .Where(r =>
                    r.TenantId == tenantId.Value &&
                    r.RequestedByUserId == requestedByUserId &&
                    r.RequestFingerprint == requestFingerprint &&
                    r.GeneratedAt >= recentSince &&
                    r.Status != "Failed")
                .OrderByDescending(r => r.GeneratedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (duplicate is null)
            {
                throw;
            }

            return MapRecentReport(duplicate);
        }

        try
        {
            await BuildDatasetAsync(run, normalizedRequest, cancellationToken);
            run.Status = "Generated";
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            run.Status = "Failed";
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorMessage = ex.Message;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return MapRecentReport(run);
    }

    public async Task<ReportDownloadFile> DownloadAsync(Guid id, string? clientIp, string? userAgent, CancellationToken cancellationToken)
    {
        var report = await _context.ReportRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Report run was not found.");

        try
        {
            var request = TryDeserializeRequest(report.RequestPayloadJson);
            var file = await BuildDownloadFileAsync(report, request, cancellationToken);

            report.Status = "Generated";
            report.ErrorMessage = null;
            report.CompletedAt ??= DateTime.UtcNow;
            report.LastDownloadedAt = DateTime.UtcNow;
            _context.ReportRunDownloadEvents.Add(new ReportRunDownloadEvent
            {
                Id = Guid.NewGuid(),
                TenantId = report.TenantId,
                ReportRunId = report.Id,
                DownloadedByUserId = _currentUserService.UserId,
                DownloadedAt = report.LastDownloadedAt.Value,
                FileName = file.FileName,
                ContentType = file.ContentType,
                ClientIp = clientIp,
                UserAgent = TruncateForStorage(userAgent, 1024)
            });
            await _context.SaveChangesAsync(cancellationToken);
            return file;
        }
        catch (Exception ex) when (ex is not KeyNotFoundException)
        {
            report.Status = "Failed";
            report.ErrorMessage = ex.Message;
            report.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Report generation failed.", ex);
        }
    }

    public async Task<ReportRunDetailDto?> GetReportRunDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var report = await _context.ReportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report == null)
        {
            return null;
        }

        var downloads = await _context.ReportRunDownloadEvents
            .AsNoTracking()
            .Where(d => d.ReportRunId == id)
            .OrderByDescending(d => d.DownloadedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var request = TryDeserializeRequest(report.RequestPayloadJson);
        return MapReportRunDetail(report, request, downloads);
    }

    public async Task<ScheduledReport> CreateScheduledAsync(CreateScheduledReportRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("No valid tenant context found.");
        }

        var item = new ReportSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            Name = request.Name.Trim(),
            ReportType = NormalizeReportType(request.ReportType, allowCustom: false),
            Format = NormalizeReportFormat(request.Format),
            Frequency = NormalizeFrequency(request.Frequency),
            NextRun = NormalizeUtc(request.NextRun),
            CreatedByUserId = _currentUserService.UserId
        };

        _context.ReportSchedules.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return MapScheduledReport(item);
    }

    public async Task<ScheduledReport?> UpdateScheduledAsync(Guid id, UpdateScheduledReportRequest request, CancellationToken cancellationToken)
    {
        var existing = await _context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (existing == null)
        {
            return null;
        }

        existing.Name = request.Name.Trim();
        existing.ReportType = NormalizeReportType(request.ReportType, allowCustom: false);
        existing.Format = NormalizeReportFormat(request.Format, existing.Format);
        existing.Frequency = NormalizeFrequency(request.Frequency);
        existing.NextRun = NormalizeUtc(request.NextRun);
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapScheduledReport(existing);
    }

    public async Task DeleteScheduledAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (existing == null)
        {
            return;
        }

        _context.ReportSchedules.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static RecentReport MapRecentReport(ReportRun run) => new()
    {
        Id = run.Id,
        Name = run.Name,
        Format = run.Format,
        ReportType = run.ReportType,
        Status = string.IsNullOrWhiteSpace(run.Status) ? "Generated" : run.Status,
        ErrorMessage = run.ErrorMessage,
        CompletedAt = run.CompletedAt,
        LastDownloadedAt = run.LastDownloadedAt,
        GeneratedAt = run.GeneratedAt
    };

    private static ScheduledReport MapScheduledReport(ReportSchedule item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        ReportType = item.ReportType,
        Format = item.Format,
        Frequency = item.Frequency,
        NextRun = item.NextRun,
        IsActive = item.IsActive,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };

    // "Impact Summary - Nov 15, 2026 11:16 AM" in the requester's time zone (UTC when unknown),
    // instead of "impact-summary-20261115-1716" in UTC.
    private static string BuildReportName(string reportType, string? timeZoneId, DateTime utcNow)
    {
        var clock = ReportClock.For(timeZoneId);
        return $"{DescribeReportType(reportType)} - {clock.Stamp(utcNow)}";
    }

    // Same names the Reports page uses, so the PDF title matches the button that made it.
    private static string DescribeReportType(string? reportType) => (reportType ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "impact-summary" => "Impact Summary",
        "investment-detail" or "investments" => "Service Cost Detail",
        "outcomes-by-category" => "Outcomes by Type",
        "funding-utilization" => "Money Source Use",
        "season-progression" => "Progress Over Time",
        "demographic-breakdown" => "People Served Breakdown",
        _ => "Custom Report"
    };

    // "FinancialStability" -> "Financial stability" for enum values shown in reports.
    private static string Humanize(object? value)
    {
        var text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var spaced = System.Text.RegularExpressions.Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return spaced.Length <= 1 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }

    // Converts stored UTC timestamps for display. Date-only fields (an outcome's date, a plan's
    // start date) are stored as UTC midnight and must NOT be shifted, or they show a day early.
    private sealed class ReportClock
    {
        private ReportClock(TimeZoneInfo zone, bool isUtc) { Zone = zone; IsUtc = isUtc; }

        public TimeZoneInfo Zone { get; }
        public bool IsUtc { get; }

        public static ReportClock For(string? timeZoneId) =>
            !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone)
                ? new ReportClock(zone, zone.BaseUtcOffset == TimeSpan.Zero && zone.Id is "UTC" or "Etc/UTC")
                : new ReportClock(TimeZoneInfo.Utc, isUtc: true);

        public DateTime Local(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        public string Stamp(DateTime utc) =>
            Local(utc).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) + (IsUtc ? " UTC" : string.Empty);

        public string Day(DateTime utc) => Local(utc).ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

        public static string CalendarDay(DateTime value) => value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    private static string BuildIdempotencyKey(string scope, Guid tenantId, Guid? userId, string fingerprint, DateTime now, TimeSpan window)
    {
        var bucketTicks = now.Ticks - (now.Ticks % window.Ticks);
        return Sha256Hex($"{scope}|{tenantId}|{userId?.ToString() ?? "system"}|{fingerprint}|{bucketTicks}");
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static GenerateReportRequest NormalizeGenerateRequest(GenerateReportRequest request, string reportType, string format)
    {
        return new GenerateReportRequest
        {
            ReportType = reportType,
            Format = format,
            FiscalYear = string.IsNullOrWhiteSpace(request.FiscalYear) ? null : request.FiscalYear.Trim(),
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            GroupBy = string.IsNullOrWhiteSpace(request.GroupBy) ? null : request.GroupBy.Trim(),
            TimeZone = !string.IsNullOrWhiteSpace(request.TimeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone.Trim(), out _)
                ? request.TimeZone.Trim()
                : null
        };
    }

    private static string NormalizeReportType(string? value, bool allowCustom, string fallback = ReportContract.ImpactSummary)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim().ToLowerInvariant();

        if (ReportContract.SupportedReportTypes.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return allowCustom ? ReportContract.CustomReport : fallback;
    }

    private static string NormalizeReportFormat(string? value, string fallback = "pdf")
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pdf" => "pdf",
            "excel" => "excel",
            "csv" => "csv",
            _ => fallback
        };
    }

    private static string NormalizeReportStatus(string? value, string fallback = "Generated")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return ReportContract.SupportedStatuses.FirstOrDefault(s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private static string NormalizeFrequency(string? value, string fallback = "Monthly")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return ReportContract.SupportedFrequencies.FirstOrDefault(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    // Not ToString("C", InvariantCulture): the invariant culture's currency symbol is the
    // generic sign "¤", which is what the PDFs showed ("¤125.00"). Culture-independent on
    // purpose so it also works where the container has no ICU culture data.
    private static string FormatMoney(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value;
    }

    private GenerateReportRequest TryDeserializeRequest(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new GenerateReportRequest();
        }

        try
        {
            return JsonSerializer.Deserialize<GenerateReportRequest>(payloadJson) ?? new GenerateReportRequest();
        }
        catch
        {
            return new GenerateReportRequest();
        }
    }

    private static ReportRunDetailDto MapReportRunDetail(ReportRun run, GenerateReportRequest request, List<ReportRunDownloadEvent> downloads)
    {
        var completedAt = run.CompletedAt;
        long? durationMs = completedAt.HasValue
            ? Math.Max(0, (long)(completedAt.Value - run.GeneratedAt).TotalMilliseconds)
            : null;

        var timeline = new List<ReportRunTimelineItemDto>
        {
            new()
            {
                EventType = "requested",
                Label = "Report requested",
                OccurredAt = run.GeneratedAt,
                Details = $"{run.ReportType} ({run.Format})"
            }
        };

        if (completedAt.HasValue)
        {
            timeline.Add(new ReportRunTimelineItemDto
            {
                EventType = string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "generated",
                Label = string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase) ? "Generation failed" : "Report generated",
                OccurredAt = completedAt.Value,
                Details = string.IsNullOrWhiteSpace(run.ErrorMessage) ? null : run.ErrorMessage
            });
        }

        foreach (var download in downloads.OrderBy(d => d.DownloadedAt))
        {
            timeline.Add(new ReportRunTimelineItemDto
            {
                EventType = "downloaded",
                Label = "Downloaded",
                OccurredAt = download.DownloadedAt,
                Details = $"{download.FileName} ({download.ContentType})"
            });
        }

        timeline = timeline.OrderByDescending(t => t.OccurredAt).ToList();

        return new ReportRunDetailDto
        {
            Id = run.Id,
            Name = run.Name,
            ReportType = run.ReportType,
            Format = run.Format,
            Status = string.IsNullOrWhiteSpace(run.Status) ? "Generated" : run.Status,
            ErrorMessage = run.ErrorMessage,
            GeneratedAt = run.GeneratedAt,
            CompletedAt = run.CompletedAt,
            LastDownloadedAt = run.LastDownloadedAt,
            DurationMs = durationMs,
            FiscalYear = request.FiscalYear,
            DateFrom = request.DateFrom,
            DateTo = request.DateTo,
            GroupBy = request.GroupBy,
            RequestPayloadJson = string.IsNullOrWhiteSpace(run.RequestPayloadJson) ? "{}" : run.RequestPayloadJson,
            Timeline = timeline,
            DownloadEvents = downloads
                .OrderByDescending(d => d.DownloadedAt)
                .Select(d => new ReportRunDownloadEventDto
                {
                    Id = d.Id,
                    DownloadedAt = d.DownloadedAt,
                    FileName = d.FileName,
                    ContentType = d.ContentType,
                    ClientIp = d.ClientIp,
                    UserAgent = d.UserAgent
                })
                .ToList()
        };
    }

    private async Task<ReportDownloadFile> BuildDownloadFileAsync(ReportRun run, GenerateReportRequest request, CancellationToken cancellationToken)
    {
        var dataset = await BuildDatasetAsync(run, request, cancellationToken);
        var normalizedFormat = (run.Format ?? "pdf").Trim().ToLowerInvariant();
        var safeName = SanitizeFileName(run.Name);

        return normalizedFormat switch
        {
            "csv" => new ReportDownloadFile(
                $"{safeName}.csv",
                "text/csv; charset=utf-8",
                Encoding.UTF8.GetBytes(BuildCsv(dataset))),
            "excel" or "xlsx" or "xls" => new ReportDownloadFile(
                $"{safeName}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                TryBuildXlsx(dataset)),
            _ => new ReportDownloadFile(
                $"{safeName}.pdf",
                "application/pdf",
                TryBuildBrandedPdf(dataset))
        };
    }

    private async Task<ReportDataset> BuildDatasetAsync(ReportRun run, GenerateReportRequest request, CancellationToken cancellationToken)
    {
        var reportType = string.IsNullOrWhiteSpace(run.ReportType) ? "custom-report" : run.ReportType.Trim().ToLowerInvariant();
        var dateFrom = request.DateFrom.HasValue ? NormalizeUtc(request.DateFrom.Value) : (DateTime?)null;
        // "To" is a calendar date: include the whole day. Using midnight as the upper bound
        // dropped everything recorded on the end date itself.
        var dateTo = request.DateTo.HasValue
            ? NormalizeUtc(request.DateTo.Value.Date.AddDays(1).AddTicks(-1))
            : (DateTime?)null;

        var clock = ReportClock.For(request.TimeZone);

        var dataset = reportType switch
        {
            "impact-summary" => await BuildImpactSummaryAsync(dateFrom, dateTo, cancellationToken),
            "investment-detail" or "investments" => await BuildInvestmentDetailAsync(dateFrom, dateTo, request.GroupBy, clock, cancellationToken),
            "outcomes-by-category" => await BuildOutcomesByCategoryAsync(dateFrom, dateTo, cancellationToken),
            "funding-utilization" => await BuildFundingUtilizationAsync(cancellationToken),
            "season-progression" => await BuildSeasonProgressionAsync(cancellationToken),
            "demographic-breakdown" => await BuildDemographicBreakdownAsync(cancellationToken),
            _ => await BuildCustomSummaryAsync(request, dateFrom, dateTo, cancellationToken)
        };

        // Every report says whose it is, which period it covers, and when it was made.
        var organizationName = await _context.Tenants.AsNoTracking()
            .Where(t => t.Id == run.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);
        dataset.Organization = string.IsNullOrWhiteSpace(organizationName) ? "Your organization" : organizationName.Trim();
        dataset.Generated = clock.Stamp(run.GeneratedAt);
        dataset.Period = DescribePeriod(request.DateFrom, request.DateTo, dataset.UsesDateRange);
        dataset.Subtitle = string.Join(" · ",
            new[] { dataset.Organization, dataset.Period, dataset.Subtitle }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return dataset;
    }

    private static string DescribePeriod(DateTime? from, DateTime? to, bool usesDateRange)
    {
        if (!usesDateRange) return string.Empty;
        return (from, to) switch
        {
            (null, null) => "All dates",
            ({ } f, null) => $"From {ReportClock.CalendarDay(f)}",
            (null, { } t) => $"Through {ReportClock.CalendarDay(t)}",
            ({ } f, { } t) => $"{ReportClock.CalendarDay(f)} to {ReportClock.CalendarDay(t)}"
        };
    }

    // A plain-language overview for funders and boards: who was served, what was provided,
    // and what changed. (It used to print five numbers twice under internal labels.)
    private async Task<ReportDataset> BuildImpactSummaryAsync(DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var investments = _context.Investments.AsNoTracking()
            .Where(i => i.Status != InvestmentStatus.Cancelled && i.Status != InvestmentStatus.Returned && i.Status != InvestmentStatus.Draft);
        var imprints = _context.Imprints.AsNoTracking().AsQueryable();

        if (dateFrom.HasValue)
        {
            investments = investments.Where(i => i.CreatedAt >= dateFrom.Value);
            imprints = imprints.Where(i => i.DateOccurred >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            investments = investments.Where(i => i.CreatedAt <= dateTo.Value);
            imprints = imprints.Where(i => i.DateOccurred <= dateTo.Value);
        }

        var serviceRows = await investments
            .Select(i => new { i.ClientId, i.Amount, ProgramName = i.Program != null ? i.Program.Name : "" })
            .ToListAsync(cancellationToken);
        var outcomeRows = await imprints
            .Select(i => new { i.ClientId, i.Category, i.Outcome })
            .ToListAsync(cancellationToken);

        var peopleServed = serviceRows.Select(r => r.ClientId).Concat(outcomeRows.Select(r => r.ClientId)).Distinct().Count();
        var improved = outcomeRows.Count(o => o.Outcome == ImpactOutcome.Improved);
        var maintained = outcomeRows.Count(o => o.Outcome == ImpactOutcome.Maintained);

        var data = new ReportDataset
        {
            Title = DescribeReportType("impact-summary"),
            Subtitle = "Who you served, what you provided, and what changed",
            UsesDateRange = true,
            Summary =
            {
                ["People served"] = peopleServed.ToString(CultureInfo.InvariantCulture),
                ["Services provided"] = serviceRows.Count.ToString(CultureInfo.InvariantCulture),
                ["Value of services"] = FormatMoney(serviceRows.Sum(r => r.Amount)),
                ["Outcomes recorded"] = outcomeRows.Count.ToString(CultureInfo.InvariantCulture),
                ["Outcomes showing improvement"] = outcomeRows.Count == 0
                    ? "0"
                    : $"{improved} of {outcomeRows.Count} ({improved * 100 / outcomeRows.Count}%)",
                ["Outcomes holding steady"] = maintained.ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Section", "Item", "How many", "Detail" }
        };

        foreach (var program in serviceRows
                     .GroupBy(r => string.IsNullOrWhiteSpace(r.ProgramName) ? "No program" : r.ProgramName)
                     .OrderByDescending(g => g.Sum(r => r.Amount)))
        {
            var people = program.Select(r => r.ClientId).Distinct().Count();
            data.Rows.Add(["What you provided", program.Key,
                $"{program.Count()} {(program.Count() == 1 ? "service" : "services")}",
                $"{FormatMoney(program.Sum(r => r.Amount))} for {people} {(people == 1 ? "person" : "people")}"]);
        }

        foreach (var category in outcomeRows.GroupBy(o => o.Category).OrderByDescending(g => g.Count()))
        {
            var categoryImproved = category.Count(o => o.Outcome == ImpactOutcome.Improved);
            data.Rows.Add(["What changed", Humanize(category.Key),
                $"{category.Count()} {(category.Count() == 1 ? "outcome" : "outcomes")}",
                $"{categoryImproved} improved"]);
        }

        if (data.Rows.Count == 0)
        {
            data.Rows.Add(["Nothing recorded yet", "Log services and outcomes to fill in this summary.", "", ""]);
        }

        return data;
    }

    private async Task<ReportDataset> BuildInvestmentDetailAsync(DateTime? dateFrom, DateTime? dateTo, string? groupBy, ReportClock clock, CancellationToken cancellationToken)
    {
        var query = _context.Investments
            .AsNoTracking()
            .Include(i => i.Client)
            .Include(i => i.Program)
            .Include(i => i.Fund)
            .Include(i => i.FamilyMember)
            .AsQueryable();

        if (dateFrom.HasValue) query = query.Where(i => i.CreatedAt >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(i => i.CreatedAt <= dateTo.Value);

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(1000)
            .Select(i => new
            {
                i.CreatedAt,
                ClientName = (i.FamilyMember != null
                    ? ((i.FamilyMember.FirstName ?? "") + " " + (i.FamilyMember.LastName ?? ""))
                    : ((i.Client != null ? i.Client.FirstName : "") + " " + (i.Client != null ? i.Client.LastName : ""))).Trim(),
                ProgramName = i.Program != null ? i.Program.Name : "",
                FundName = i.Fund != null ? i.Fund.Name : "",
                LifePhase = i.Client != null ? i.Client.LifePhase.ToString() : "",
                i.Amount,
                i.Status,
                i.Reason,
                i.PayeeName
            })
            .ToListAsync(cancellationToken);

        // "Organize by" from the report builder. It used to be accepted and then ignored.
        var (groupLabel, groupKey) = (groupBy ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "category" => ("Program", (Func<string, string, string, DateTime, string>)((program, _, _, _) => program)),
            "funding-source" => ("Money source", (_, fund, _, _) => fund),
            "season" => ("Progress stage", (_, _, phase, _) => phase),
            "month" => ("Month", (_, _, _, created) => created.ToString("yyyy-MM (MMMM)", CultureInfo.InvariantCulture)),
            _ => (string.Empty, (Func<string, string, string, DateTime, string>?)null)
        };

        string GroupOf(string program, string fund, string phase, DateTime created)
        {
            var value = groupKey!(program, fund, phase, created);
            return string.IsNullOrWhiteSpace(value) ? "Not set" : value;
        }

        if (groupKey is not null)
        {
            rows = rows
                .OrderBy(r => GroupOf(r.ProgramName, r.FundName, r.LifePhase, r.CreatedAt), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();
        }

        var data = new ReportDataset
        {
            Title = DescribeReportType("investment-detail"),
            Subtitle = "Every service provided, what it cost, and which money source paid for it",
            UsesDateRange = true,
            Summary =
            {
                ["Service records"] = rows.Count.ToString(CultureInfo.InvariantCulture),
                ["Total cost"] = FormatMoney(rows.Sum(r => r.Amount))
            },
            Columns = { "Date", "Person", "Program", "Money source", "Paid to", "Amount", "Status", "What was provided" }
        };

        if (groupKey is not null)
        {
            data.Summary["Organized by"] = groupLabel;
            foreach (var group in rows.GroupBy(r => GroupOf(r.ProgramName, r.FundName, r.LifePhase, r.CreatedAt), StringComparer.OrdinalIgnoreCase))
            {
                var count = group.Count();
                data.Summary[$"{groupLabel}: {group.Key}"] =
                    $"{FormatMoney(group.Sum(r => r.Amount))} ({count} {(count == 1 ? "record" : "records")})";
            }

            data.Columns.Insert(0, groupLabel);
        }

        foreach (var row in rows)
        {
            var cells = new List<string>
            {
                clock.Day(row.CreatedAt),
                string.IsNullOrWhiteSpace(row.ClientName) ? "Unknown" : row.ClientName,
                row.ProgramName,
                row.FundName,
                row.PayeeName,
                FormatMoney(row.Amount),
                Humanize(row.Status),
                row.Reason
            };

            if (groupKey is not null)
            {
                cells.Insert(0, GroupOf(row.ProgramName, row.FundName, row.LifePhase, row.CreatedAt));
            }

            data.Rows.Add(cells);
        }

        return data;
    }

    private async Task<ReportDataset> BuildOutcomesByCategoryAsync(DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var query = _context.Imprints.AsNoTracking().AsQueryable();
        if (dateFrom.HasValue) query = query.Where(i => i.DateOccurred >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(i => i.DateOccurred <= dateTo.Value);

        var grouped = await query
            .GroupBy(i => new { i.Category, i.Outcome })
            .Select(g => new
            {
                g.Key.Category,
                g.Key.Outcome,
                Count = g.Count(),
                Latest = g.Max(x => x.DateOccurred)
            })
            .ToListAsync(cancellationToken);
        grouped = grouped.OrderByDescending(x => x.Count).ThenBy(x => Humanize(x.Category)).ToList();

        var data = new ReportDataset
        {
            Title = DescribeReportType("outcomes-by-category"),
            Subtitle = "What changed for the people you serve, by type of outcome",
            UsesDateRange = true,
            Summary =
            {
                ["Outcomes recorded"] = grouped.Sum(g => g.Count).ToString(CultureInfo.InvariantCulture),
                ["Types of outcome"] = grouped.Select(g => g.Category).Distinct().Count().ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Type", "Result", "How many", "Most recent" }
        };

        foreach (var row in grouped)
        {
            data.Rows.Add(new List<string>
            {
                Humanize(row.Category),
                Humanize(row.Outcome),
                row.Count.ToString(CultureInfo.InvariantCulture),
                // DateOccurred is a calendar date stored as UTC midnight: format it, don't shift it.
                ReportClock.CalendarDay(row.Latest)
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildFundingUtilizationAsync(CancellationToken cancellationToken)
    {
        var funds = await _context.Funds
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new
            {
                f.Name,
                f.TotalAmount,
                f.AvailableAmount
            })
            .ToListAsync(cancellationToken);

        var data = new ReportDataset
        {
            Title = DescribeReportType("funding-utilization"),
            Subtitle = "How much of each money source has been used and how much is left",
            Summary =
            {
                ["Money sources"] = funds.Count.ToString(CultureInfo.InvariantCulture),
                ["Total received"] = FormatMoney(funds.Sum(f => f.TotalAmount)),
                ["Still available"] = FormatMoney(funds.Sum(f => f.AvailableAmount))
            },
            Columns = { "Money source", "Total", "Still available", "Used", "Percent used" }
        };

        foreach (var fund in funds)
        {
            var deployed = fund.TotalAmount - fund.AvailableAmount;
            var utilizationPct = fund.TotalAmount <= 0m ? 0m : (deployed / fund.TotalAmount) * 100m;
            data.Rows.Add(new List<string>
            {
                fund.Name,
                FormatMoney(fund.TotalAmount),
                FormatMoney(fund.AvailableAmount),
                FormatMoney(deployed),
                utilizationPct.ToString("F1", CultureInfo.InvariantCulture) + "%"
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildSeasonProgressionAsync(CancellationToken cancellationToken)
    {
        var plans = await _context.GrowthPlans
            .AsNoTracking()
            .Include(g => g.Client)
            .OrderByDescending(g => g.StartDate)
            .Take(1000)
            .Select(g => new
            {
                g.Title,
                g.Season,
                g.Status,
                g.StartDate,
                g.TargetEndDate,
                g.CompletedGoals,
                g.TotalGoals,
                ClientName = g.Client != null ? (g.Client.FirstName + " " + g.Client.LastName).Trim() : string.Empty
            })
            .ToListAsync(cancellationToken);

        var data = new ReportDataset
        {
            Title = DescribeReportType("season-progression"),
            Subtitle = "Growth plans by stage, with goals completed",
            Summary =
            {
                ["Growth plans"] = plans.Count.ToString(CultureInfo.InvariantCulture),
                ["Goals completed"] = plans.Sum(p => p.CompletedGoals).ToString(CultureInfo.InvariantCulture),
                ["Goals set"] = plans.Sum(p => p.TotalGoals).ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Plan", "Person", "Season", "Status", "Start", "Target end", "Goals", "Percent complete" }
        };

        foreach (var plan in plans)
        {
            var completion = plan.TotalGoals <= 0 ? 0m : (decimal)plan.CompletedGoals / plan.TotalGoals * 100m;
            data.Rows.Add(new List<string>
            {
                plan.Title,
                string.IsNullOrWhiteSpace(plan.ClientName) ? "Unassigned" : plan.ClientName,
                Humanize(plan.Season),
                Humanize(plan.Status),
                ReportClock.CalendarDay(plan.StartDate),
                plan.TargetEndDate is { } end ? ReportClock.CalendarDay(end) : string.Empty,
                $"{plan.CompletedGoals}/{plan.TotalGoals}",
                completion.ToString("F0", CultureInfo.InvariantCulture) + "%"
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildDemographicBreakdownAsync(CancellationToken cancellationToken)
    {
        var clients = await _context.Clients
            .AsNoTracking()
            .Select(c => new
            {
                c.LifePhase,
                c.EmploymentStatus,
                c.HouseholdCount,
                c.StabilityScore
            })
            .ToListAsync(cancellationToken);

        var phaseGroups = clients.GroupBy(c => c.LifePhase)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new
            {
                Group = $"Progress stage: {Humanize(g.Key)}",
                Count = g.Count(),
                AvgStability = g.Average(x => x.StabilityScore),
                AvgHousehold = g.Average(x => x.HouseholdCount)
            });

        var employmentGroups = clients.GroupBy(c => c.EmploymentStatus)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new
            {
                Group = $"Employment: {Humanize(g.Key)}",
                Count = g.Count(),
                AvgStability = g.Average(x => x.StabilityScore),
                AvgHousehold = g.Average(x => x.HouseholdCount)
            });

        var combined = phaseGroups.Concat(employmentGroups).ToList();

        var data = new ReportDataset
        {
            Title = DescribeReportType("demographic-breakdown"),
            Subtitle = "The people you serve, by progress stage and employment",
            Summary =
            {
                ["People"] = clients.Count.ToString(CultureInfo.InvariantCulture),
                ["Average stability score"] = (clients.Count == 0 ? 0d : clients.Average(c => c.StabilityScore)).ToString("F1", CultureInfo.InvariantCulture)
            },
            Columns = { "Group", "People", "Average stability", "Average household size" }
        };

        foreach (var row in combined)
        {
            data.Rows.Add(new List<string>
            {
                row.Group,
                row.Count.ToString(CultureInfo.InvariantCulture),
                row.AvgStability.ToString("F1", CultureInfo.InvariantCulture),
                row.AvgHousehold.ToString("F1", CultureInfo.InvariantCulture)
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildCustomSummaryAsync(GenerateReportRequest request, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var dataset = await BuildImpactSummaryAsync(dateFrom, dateTo, cancellationToken);
        dataset.Title = DescribeReportType("custom-report");
        if (!string.IsNullOrWhiteSpace(request.FiscalYear))
        {
            dataset.Summary["Fiscal year"] = request.FiscalYear!;
        }
        return dataset;
    }

    private static string BuildCsv(ReportDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine(EscapeCsv(dataset.Title));
        if (!string.IsNullOrWhiteSpace(dataset.Subtitle))
        {
            sb.AppendLine(EscapeCsv(dataset.Subtitle));
        }
        if (!string.IsNullOrWhiteSpace(dataset.Generated))
        {
            sb.AppendLine(EscapeCsv($"Created {dataset.Generated}"));
        }
        sb.AppendLine();

        if (dataset.Summary.Count > 0)
        {
            sb.AppendLine("Metric,Value");
            foreach (var item in dataset.Summary)
            {
                sb.Append(EscapeCsv(item.Key)).Append(',').Append(EscapeCsv(item.Value)).AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine(string.Join(",", dataset.Columns.Select(EscapeCsv)));
        foreach (var row in dataset.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return sb.ToString();
    }

    private static string BuildExcelHtml(ReportDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><head><meta charset=\"utf-8\" />");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;color:#1f2937} table{border-collapse:collapse;width:100%;margin-top:12px} th,td{border:1px solid #cbd5e1;padding:6px 8px;text-align:left} th{background:#e2e8f0} h1{font-size:18px;margin:0 0 8px} h2{font-size:13px;color:#475569;margin:0 0 12px} .summary td:first-child{font-weight:600;background:#f8fafc;width:220px}</style>");
        sb.AppendLine("</head><body>");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(dataset.Title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(dataset.Subtitle))
        {
            sb.Append("<h2>").Append(WebUtility.HtmlEncode(dataset.Subtitle)).AppendLine("</h2>");
        }

        if (dataset.Summary.Count > 0)
        {
            sb.AppendLine("<table class=\"summary\"><tbody>");
            foreach (var item in dataset.Summary)
            {
                sb.Append("<tr><td>")
                    .Append(WebUtility.HtmlEncode(item.Key))
                    .Append("</td><td>")
                    .Append(WebUtility.HtmlEncode(item.Value))
                    .AppendLine("</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine("<table><thead><tr>");
        foreach (var col in dataset.Columns)
        {
            sb.Append("<th>").Append(WebUtility.HtmlEncode(col)).AppendLine("</th>");
        }
        sb.AppendLine("</tr></thead><tbody>");
        foreach (var row in dataset.Rows)
        {
            sb.AppendLine("<tr>");
            foreach (var cell in row)
            {
                sb.Append("<td>").Append(WebUtility.HtmlEncode(cell)).AppendLine("</td>");
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static byte[] TryBuildXlsx(ReportDataset dataset)
    {
        try
        {
            return BuildXlsx(dataset);
        }
        catch
        {
            // Fallback to Excel-compatible HTML if package generation fails unexpectedly.
            return Encoding.UTF8.GetBytes(BuildExcelHtml(dataset));
        }
    }

    private static byte[] BuildXlsx(ReportDataset dataset)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Report");

        var maxColumns = Math.Max(1, dataset.Columns.Count);
        var row = 1;

        sheet.Cell(row, 1).Value = dataset.Title;
        sheet.Range(row, 1, row, maxColumns).Merge();
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 16;
        sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#32505A");
        row++;

        if (!string.IsNullOrWhiteSpace(dataset.Subtitle))
        {
            sheet.Cell(row, 1).Value = dataset.Subtitle;
            sheet.Range(row, 1, row, maxColumns).Merge();
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#475569");
            row++;
        }

        if (!string.IsNullOrWhiteSpace(dataset.Generated))
        {
            sheet.Cell(row, 1).Value = $"Created {dataset.Generated}";
            sheet.Range(row, 1, row, maxColumns).Merge();
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#475569");
            row++;
        }

        row++;

        if (dataset.Summary.Count > 0)
        {
            sheet.Cell(row, 1).Value = "Metric";
            sheet.Cell(row, 2).Value = "Value";
            sheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            sheet.Range(row, 1, row, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            row++;

            foreach (var item in dataset.Summary)
            {
                sheet.Cell(row, 1).Value = item.Key;
                sheet.Cell(row, 2).Value = item.Value;
                row++;
            }

            row++;
        }

        for (var i = 0; i < dataset.Columns.Count; i++)
        {
            sheet.Cell(row, i + 1).Value = dataset.Columns[i];
        }

        sheet.Range(row, 1, row, maxColumns).Style.Font.Bold = true;
        sheet.Range(row, 1, row, maxColumns).Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
        row++;

        foreach (var dataRow in dataset.Rows)
        {
            for (var col = 0; col < dataRow.Count; col++)
            {
                sheet.Cell(row, col + 1).Value = dataRow[col];
            }
            row++;
        }

        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string BuildPlainText(ReportDataset dataset)
    {
        var lines = new List<string>
        {
            dataset.Title
        };

        if (!string.IsNullOrWhiteSpace(dataset.Subtitle))
        {
            lines.Add(dataset.Subtitle);
        }

        if (!string.IsNullOrWhiteSpace(dataset.Generated))
        {
            lines.Add($"Created {dataset.Generated}");
        }

        lines.Add(string.Empty);

        foreach (var item in dataset.Summary)
        {
            lines.Add($"{item.Key}: {item.Value}");
        }

        if (dataset.Summary.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add(string.Join(" | ", dataset.Columns));
        lines.Add(new string('-', Math.Min(110, Math.Max(20, string.Join(" | ", dataset.Columns).Length))));

        foreach (var row in dataset.Rows.Take(80))
        {
            lines.Add(string.Join(" | ", row));
        }

        if (dataset.Rows.Count > 80)
        {
            lines.Add($"... {dataset.Rows.Count - 80} additional rows omitted in PDF preview output.");
        }

        return string.Join('\n', lines);
    }

    private static byte[] TryBuildBrandedPdf(ReportDataset dataset)
    {
        try
        {
            return BuildBrandedPdf(dataset);
        }
        catch
        {
            return BuildPdf(BuildPlainText(dataset));
        }
    }

    private static byte[] BuildBrandedPdf(ReportDataset dataset)
    {
        var rows = dataset.Rows.Take(120).ToList();

        return QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(text =>
                        {
                            text.Span("grow").FontColor("#32505A").SemiBold().FontSize(18);
                            text.Span(".IT").FontColor("#27B062").SemiBold().FontSize(18);
                        });

                        r.ConstantItem(220).AlignRight().Column(meta =>
                        {
                            if (!string.IsNullOrWhiteSpace(dataset.Organization))
                            {
                                meta.Item().AlignRight().Text(dataset.Organization).SemiBold().FontColor("#32505A");
                            }
                            if (!string.IsNullOrWhiteSpace(dataset.Generated))
                            {
                                meta.Item().AlignRight().Text($"Created {dataset.Generated}").FontColor(Colors.Grey.Darken2);
                            }
                        });
                    });

                    col.Item().PaddingTop(8).Text(dataset.Title).FontSize(15).SemiBold().FontColor("#32505A");
                    if (!string.IsNullOrWhiteSpace(dataset.Subtitle))
                    {
                        col.Item().Text(dataset.Subtitle).FontColor(Colors.Grey.Darken2);
                    }
                    col.Item().PaddingTop(10).LineHorizontal(1).LineColor("#CBD5E1");
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    if (dataset.Summary.Count > 0)
                    {
                        col.Item().Text("At a glance").SemiBold().FontColor("#32505A");
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                columns.RelativeColumn(3);
                            });

                            foreach (var item in dataset.Summary)
                            {
                                table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4)
                                    .Text(item.Key).SemiBold();
                                table.Cell().BorderBottom(0.5f).BorderColor("#E2E8F0").Padding(4)
                                    .Text(item.Value ?? string.Empty);
                            }
                        });

                        col.Item().PaddingTop(12);
                    }

                    if (dataset.Columns.Count > 0)
                    {
                        col.Item().Text("Details").SemiBold().FontColor("#32505A");
                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                for (var i = 0; i < dataset.Columns.Count; i++)
                                {
                                    columns.RelativeColumn();
                                }
                            });

                            foreach (var header in dataset.Columns)
                            {
                                table.Cell().Background("#E8F5EE").Padding(4).Border(0.5f).BorderColor("#CFE7DB")
                                    .Text(header).SemiBold();
                            }

                            foreach (var row in rows)
                            {
                                foreach (var cell in row)
                                {
                                    table.Cell().Padding(4).BorderBottom(0.5f).BorderColor("#EEF2F7")
                                        .Text(cell ?? string.Empty);
                                }
                            }
                        });

                        if (dataset.Rows.Count > rows.Count)
                        {
                            col.Item().PaddingTop(6).Text(
                                    $"Showing {rows.Count} of {dataset.Rows.Count} rows in PDF preview. Export Excel or CSV for the full dataset.")
                                .FontColor(Colors.Grey.Darken1)
                                .FontSize(9);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("grow").FontColor("#32505A").SemiBold();
                    text.Span(".IT").FontColor("#27B062").SemiBold();
                    text.Span("  |  Plant the seed. Measure the growth. Build the future.")
                        .FontColor(Colors.Grey.Darken2);
                });
            });
        }).GeneratePdf();
    }

    private static byte[] BuildPdf(string text)
    {
        var lines = text.Replace("\r", string.Empty)
            .Split('\n')
            .Select(l => l.Length > 120 ? l[..120] : l)
            .Take(55)
            .ToList();

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine("BT");
        contentBuilder.AppendLine("/F1 10 Tf");
        contentBuilder.AppendLine("14 TL");
        contentBuilder.AppendLine("40 800 Td");

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) contentBuilder.AppendLine("T*");
            contentBuilder.Append('(').Append(EscapePdf(lines[i])).AppendLine(") Tj");
        }

        contentBuilder.AppendLine("ET");
        var contentBytes = Encoding.ASCII.GetBytes(contentBuilder.ToString());

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true);
        var offsets = new List<long>();

        void WriteObject(string body)
        {
            offsets.Add(stream.Position);
            writer.WriteLine($"{offsets.Count} 0 obj");
            writer.Write(body);
            if (!body.EndsWith('\n')) writer.WriteLine();
            writer.WriteLine("endobj");
            writer.Flush();
        }

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        WriteObject("<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        WriteObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        WriteObject($"<< /Length {contentBytes.Length} >>\nstream\n{Encoding.ASCII.GetString(contentBytes)}endstream");

        var xrefPosition = stream.Position;
        writer.WriteLine($"xref\n0 {offsets.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");
        foreach (var offset in offsets)
        {
            writer.WriteLine($"{offset:D10} 00000 n ");
        }

        writer.WriteLine($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>");
        writer.WriteLine($"startxref\n{xrefPosition}");
        writer.WriteLine("%%EOF");
        writer.Flush();

        return stream.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        var escaped = value.Replace("\"", "\"\"");
        return escaped.Contains(',') || escaped.Contains('"') || escaped.Contains('\n') || escaped.Contains('\r')
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string EscapePdf(string value) =>
        value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string? TruncateForStorage(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class ReportDataset
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string Generated { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public bool UsesDateRange { get; set; }
        public Dictionary<string, string> Summary { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Columns { get; } = new();
        public List<List<string>> Rows { get; } = new();
    }

    public sealed record ReportDownloadFile(string FileName, string ContentType, byte[] Bytes);
}
