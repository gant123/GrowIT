using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace GrowIT.API.Controllers;

[Authorize(Policy = "AdminOrManager")]
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public ReportsController(ApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpGet("recent")]
    public async Task<ActionResult<List<RecentReport>>> GetRecentReports([FromQuery] RecentReportsQueryParams query)
    {
        var runs = _context.ReportRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            runs = runs.Where(r => r.Name.Contains(search) || r.ReportType.Contains(search) || r.Format.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.ReportType))
        {
            var reportType = query.ReportType.Trim();
            runs = runs.Where(r => r.ReportType == reportType);
        }

        if (!string.IsNullOrWhiteSpace(query.Format))
        {
            var format = query.Format.Trim();
            runs = runs.Where(r => r.Format == format);
        }

        if (query.DateFrom.HasValue)
        {
            runs = runs.Where(r => r.GeneratedAt >= query.DateFrom.Value);
        }

        if (query.DateTo.HasValue)
        {
            // inclusive end date for date-only UI inputs
            var inclusiveEnd = query.DateTo.Value.Date.AddDays(1).AddTicks(-1);
            runs = runs.Where(r => r.GeneratedAt <= inclusiveEnd);
        }

        var take = Math.Clamp(query.Take ?? 200, 1, 1000);

        var items = await runs
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
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            items = items
                .Where(i => string.Equals(i.Status, query.Status, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return Ok(items);
    }

    [HttpGet("scheduled")]
    public async Task<ActionResult<List<ScheduledReport>>> GetScheduledReports([FromQuery] ScheduledReportsQueryParams query)
    {
        var schedules = _context.ReportSchedules
            .AsNoTracking()
            .AsQueryable();

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
            var frequency = query.Frequency.Trim();
            schedules = schedules.Where(s => s.Frequency == frequency);
        }

        var take = Math.Clamp(query.Take ?? 200, 1, 1000);

        var items = await schedules
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
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<RecentReport>> Generate([FromBody] GenerateReportRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var run = new ReportRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            Name = BuildReportName(request),
            Format = string.IsNullOrWhiteSpace(request.Format) ? "pdf" : request.Format!.Trim(),
            ReportType = string.IsNullOrWhiteSpace(request.ReportType) ? "custom-report" : request.ReportType.Trim(),
            RequestPayloadJson = JsonSerializer.Serialize(request),
            Status = "Queued",
            RequestedByUserId = _currentUserService.UserId,
            GeneratedAt = DateTime.UtcNow
        };

        _context.ReportRuns.Add(run);

        try
        {
            // Validate generation path and precompute status without storing the file payload.
            await BuildDatasetAsync(run, request, HttpContext.RequestAborted);
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

        await _context.SaveChangesAsync();

        return Ok(new RecentReport
        {
            Id = run.Id,
            Name = run.Name,
            Format = run.Format,
            ReportType = run.ReportType,
            Status = run.Status,
            ErrorMessage = run.ErrorMessage,
            CompletedAt = run.CompletedAt,
            LastDownloadedAt = run.LastDownloadedAt,
            GeneratedAt = run.GeneratedAt
        });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var report = await _context.ReportRuns
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report == null)
        {
            return NotFound();
        }

        try
        {
            var request = TryDeserializeRequest(report.RequestPayloadJson);
            var file = await BuildDownloadFileAsync(report, request, HttpContext.RequestAborted);

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
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = TruncateForStorage(Request.Headers.UserAgent.ToString(), 1024)
            });
            await _context.SaveChangesAsync(HttpContext.RequestAborted);

            return File(file.Bytes, file.ContentType, file.FileName);
        }
        catch (Exception ex)
        {
            report.Status = "Failed";
            report.ErrorMessage = ex.Message;
            report.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(HttpContext.RequestAborted);
            return Problem(
                title: "Report generation failed",
                detail: "The report could not be generated. Please review the run details and try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportRunDetailDto>> GetReportRun(Guid id)
    {
        var report = await _context.ReportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report == null)
        {
            return NotFound();
        }

        var downloads = await _context.ReportRunDownloadEvents
            .AsNoTracking()
            .Where(d => d.ReportRunId == id)
            .OrderByDescending(d => d.DownloadedAt)
            .Take(200)
            .ToListAsync();

        var request = TryDeserializeRequest(report.RequestPayloadJson);
        return Ok(MapReportRunDetail(report, request, downloads));
    }

    [HttpPost("scheduled")]
    public async Task<ActionResult<ScheduledReport>> CreateScheduled([FromBody] CreateScheduledReportRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(request.Frequency))
            return BadRequest("Frequency is required.");

        var item = new ReportSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            Name = request.Name.Trim(),
            ReportType = NormalizeReportType(request.ReportType),
            Format = NormalizeReportFormat(request.Format),
            Frequency = request.Frequency.Trim(),
            NextRun = request.NextRun,
            CreatedByUserId = _currentUserService.UserId
        };

        _context.ReportSchedules.Add(item);
        await _context.SaveChangesAsync();

        return Ok(new ScheduledReport
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
        });
    }

    [HttpPut("scheduled/{id:guid}")]
    public async Task<ActionResult<ScheduledReport>> UpdateScheduled(Guid id, [FromBody] UpdateScheduledReportRequest request)
    {
        var existing = await _context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null)
            return NotFound();

        existing.Name = string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name.Trim();
        existing.ReportType = NormalizeReportType(request.ReportType, existing.ReportType);
        existing.Format = NormalizeReportFormat(request.Format, existing.Format);
        existing.Frequency = string.IsNullOrWhiteSpace(request.Frequency) ? existing.Frequency : request.Frequency.Trim();
        existing.NextRun = request.NextRun;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new ScheduledReport
        {
            Id = existing.Id,
            Name = existing.Name,
            ReportType = existing.ReportType,
            Format = existing.Format,
            Frequency = existing.Frequency,
            NextRun = existing.NextRun,
            IsActive = existing.IsActive,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt
        });
    }

    [HttpDelete("scheduled/{id:guid}")]
    public async Task<IActionResult> DeleteScheduled(Guid id)
    {
        var existing = await _context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null)
            return NoContent();

        _context.ReportSchedules.Remove(existing);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static string BuildReportName(GenerateReportRequest request)
    {
        var type = string.IsNullOrWhiteSpace(request.ReportType) ? "custom-report" : request.ReportType.Trim();
        return $"{type}-{DateTime.UtcNow:yyyyMMdd-HHmm}";
    }

    private static string NormalizeReportType(string? value, string fallback = "impact-summary")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim();
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
        var dateFrom = request.DateFrom?.ToUniversalTime();
        var dateTo = request.DateTo?.ToUniversalTime();

        return reportType switch
        {
            "impact-summary" => await BuildImpactSummaryAsync(run, dateFrom, dateTo, cancellationToken),
            "investment-detail" or "investments" => await BuildInvestmentDetailAsync(run, dateFrom, dateTo, cancellationToken),
            "outcomes-by-category" => await BuildOutcomesByCategoryAsync(run, dateFrom, dateTo, cancellationToken),
            "funding-utilization" => await BuildFundingUtilizationAsync(run, cancellationToken),
            "season-progression" => await BuildSeasonProgressionAsync(run, cancellationToken),
            "demographic-breakdown" => await BuildDemographicBreakdownAsync(run, cancellationToken),
            _ => await BuildCustomSummaryAsync(run, request, dateFrom, dateTo, cancellationToken)
        };
    }

    private async Task<ReportDataset> BuildImpactSummaryAsync(ReportRun run, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var investments = _context.Investments.AsNoTracking().AsQueryable();
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

        var totalInvestment = await investments.SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;
        var investmentCount = await investments.CountAsync(cancellationToken);
        var householdsServed = await investments.Select(i => i.ClientId).Distinct().CountAsync(cancellationToken);
        var imprintsCount = await imprints.CountAsync(cancellationToken);
        var approvedInvestment = await investments.Where(i => i.Status == GrowIT.Shared.Enums.InvestmentStatus.Approved)
            .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

        var data = new ReportDataset
        {
            Title = "Seed to Harvest Summary",
            Subtitle = "Portfolio-level service and funding readiness metrics",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Report Type"] = run.ReportType,
                ["Total Service Investments"] = investmentCount.ToString(CultureInfo.InvariantCulture),
                ["Capital Deployed"] = totalInvestment.ToString("C", CultureInfo.InvariantCulture),
                ["Approved Capital"] = approvedInvestment.ToString("C", CultureInfo.InvariantCulture),
                ["Households Reached"] = householdsServed.ToString(CultureInfo.InvariantCulture),
                ["Impact Records"] = imprintsCount.ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Metric", "Value" }
        };

        data.Rows.Add(["Total Service Investments", investmentCount.ToString(CultureInfo.InvariantCulture)]);
        data.Rows.Add(["Capital Deployed", totalInvestment.ToString("F2", CultureInfo.InvariantCulture)]);
        data.Rows.Add(["Approved Capital", approvedInvestment.ToString("F2", CultureInfo.InvariantCulture)]);
        data.Rows.Add(["Households Reached", householdsServed.ToString(CultureInfo.InvariantCulture)]);
        data.Rows.Add(["Impact Records", imprintsCount.ToString(CultureInfo.InvariantCulture)]);

        return data;
    }

    private async Task<ReportDataset> BuildInvestmentDetailAsync(ReportRun run, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
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
                i.Amount,
                i.Status,
                i.Reason
            })
            .ToListAsync(cancellationToken);

        var data = new ReportDataset
        {
            Title = "Financial Intelligence Detail",
            Subtitle = "Service investment line items for repeatability and cost modeling",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Rows"] = rows.Count.ToString(CultureInfo.InvariantCulture),
                ["Total Amount"] = rows.Sum(r => r.Amount).ToString("C", CultureInfo.InvariantCulture)
            },
            Columns = { "Date (UTC)", "Person", "Program", "Fund", "Amount", "Status", "Purpose" }
        };

        foreach (var row in rows)
        {
            data.Rows.Add(new List<string>
            {
                row.CreatedAt.ToString("u"),
                string.IsNullOrWhiteSpace(row.ClientName) ? "Unknown" : row.ClientName,
                row.ProgramName,
                row.FundName,
                row.Amount.ToString("F2", CultureInfo.InvariantCulture),
                row.Status.ToString(),
                row.Reason
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildOutcomesByCategoryAsync(ReportRun run, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var query = _context.Imprints.AsNoTracking().AsQueryable();
        if (dateFrom.HasValue) query = query.Where(i => i.DateOccurred >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(i => i.DateOccurred <= dateTo.Value);

        var grouped = await query
            .GroupBy(i => new { i.Category, i.Outcome })
            .Select(g => new
            {
                Category = g.Key.Category.ToString(),
                Outcome = g.Key.Outcome.ToString(),
                Count = g.Count(),
                Latest = g.Max(x => x.DateOccurred)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Category)
            .ToListAsync(cancellationToken);

        var data = new ReportDataset
        {
            Title = "Outcomes by Category",
            Subtitle = "Verified outcome distribution across recorded impact events",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Category / Outcome Groups"] = grouped.Count.ToString(CultureInfo.InvariantCulture),
                ["Total Outcome Records"] = grouped.Sum(g => g.Count).ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Category", "Outcome", "Count", "Latest Event (UTC)" }
        };

        foreach (var row in grouped)
        {
            data.Rows.Add(new List<string>
            {
                row.Category,
                row.Outcome,
                row.Count.ToString(CultureInfo.InvariantCulture),
                row.Latest.ToString("u")
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildFundingUtilizationAsync(ReportRun run, CancellationToken cancellationToken)
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
            Title = "Funding Utilization",
            Subtitle = "Available vs deployed capital across funding sources",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Funds"] = funds.Count.ToString(CultureInfo.InvariantCulture),
                ["Total Capital"] = funds.Sum(f => f.TotalAmount).ToString("C", CultureInfo.InvariantCulture),
                ["Available Capital"] = funds.Sum(f => f.AvailableAmount).ToString("C", CultureInfo.InvariantCulture)
            },
            Columns = { "Fund", "Total", "Available", "Deployed", "Utilization %" }
        };

        foreach (var fund in funds)
        {
            var deployed = fund.TotalAmount - fund.AvailableAmount;
            var utilizationPct = fund.TotalAmount <= 0m ? 0m : (deployed / fund.TotalAmount) * 100m;
            data.Rows.Add(new List<string>
            {
                fund.Name,
                fund.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
                fund.AvailableAmount.ToString("F2", CultureInfo.InvariantCulture),
                deployed.ToString("F2", CultureInfo.InvariantCulture),
                utilizationPct.ToString("F1", CultureInfo.InvariantCulture)
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildSeasonProgressionAsync(ReportRun run, CancellationToken cancellationToken)
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
            Title = "Season Progression",
            Subtitle = "Growth plan progression by season and completion rates",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Growth Plans"] = plans.Count.ToString(CultureInfo.InvariantCulture),
                ["Completed Goals"] = plans.Sum(p => p.CompletedGoals).ToString(CultureInfo.InvariantCulture),
                ["Total Goals"] = plans.Sum(p => p.TotalGoals).ToString(CultureInfo.InvariantCulture)
            },
            Columns = { "Plan", "Client", "Season", "Status", "Start", "Target End", "Goals", "Completion %" }
        };

        foreach (var plan in plans)
        {
            var completion = plan.TotalGoals <= 0 ? 0m : (decimal)plan.CompletedGoals / plan.TotalGoals * 100m;
            data.Rows.Add(new List<string>
            {
                plan.Title,
                string.IsNullOrWhiteSpace(plan.ClientName) ? "Unassigned" : plan.ClientName,
                plan.Season.ToString(),
                plan.Status.ToString(),
                plan.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                plan.TargetEndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
                $"{plan.CompletedGoals}/{plan.TotalGoals}",
                completion.ToString("F1", CultureInfo.InvariantCulture)
            });
        }

        return data;
    }

    private async Task<ReportDataset> BuildDemographicBreakdownAsync(ReportRun run, CancellationToken cancellationToken)
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
                Group = $"Life Phase: {g.Key}",
                Count = g.Count(),
                AvgStability = g.Average(x => x.StabilityScore),
                AvgHousehold = g.Average(x => x.HouseholdCount)
            });

        var employmentGroups = clients.GroupBy(c => c.EmploymentStatus)
            .OrderBy(g => g.Key.ToString())
            .Select(g => new
            {
                Group = $"Employment: {g.Key}",
                Count = g.Count(),
                AvgStability = g.Average(x => x.StabilityScore),
                AvgHousehold = g.Average(x => x.HouseholdCount)
            });

        var combined = phaseGroups.Concat(employmentGroups).ToList();

        var data = new ReportDataset
        {
            Title = "Demographic Breakdown",
            Subtitle = "Portfolio segmentation for readiness and capacity planning",
            Summary =
            {
                ["Generated (UTC)"] = run.GeneratedAt.ToString("u"),
                ["Case Files"] = clients.Count.ToString(CultureInfo.InvariantCulture),
                ["Average Stability"] = (clients.Count == 0 ? 0d : clients.Average(c => c.StabilityScore)).ToString("F1", CultureInfo.InvariantCulture)
            },
            Columns = { "Segment", "Count", "Avg Stability", "Avg Household Size" }
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

    private async Task<ReportDataset> BuildCustomSummaryAsync(ReportRun run, GenerateReportRequest request, DateTime? dateFrom, DateTime? dateTo, CancellationToken cancellationToken)
    {
        var dataset = await BuildImpactSummaryAsync(run, dateFrom, dateTo, cancellationToken);
        dataset.Title = "Custom Funding Readiness Report";
        dataset.Subtitle = "Custom report request metadata and portfolio metrics";
        dataset.Summary["Requested Format"] = string.IsNullOrWhiteSpace(request.Format) ? run.Format : request.Format!;
        dataset.Summary["Requested Grouping"] = string.IsNullOrWhiteSpace(request.GroupBy) ? "none" : request.GroupBy!;
        if (!string.IsNullOrWhiteSpace(request.FiscalYear))
        {
            dataset.Summary["Fiscal Year"] = request.FiscalYear!;
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

                        r.ConstantItem(170).AlignRight().Column(meta =>
                        {
                            if (dataset.Summary.TryGetValue("Generated (UTC)", out var generated))
                            {
                                meta.Item().Text($"Generated: {generated}").FontColor(Colors.Grey.Darken2);
                            }
                            meta.Item().Text("Funding Readiness Report").FontColor(Colors.Grey.Darken2);
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
                        col.Item().Text("Executive Summary").SemiBold().FontColor("#32505A");
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
                        col.Item().Text("Report Data").SemiBold().FontColor("#32505A");
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
        public Dictionary<string, string> Summary { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Columns { get; } = new();
        public List<List<string>> Rows { get; } = new();
    }

    private sealed record ReportDownloadFile(string FileName, string ContentType, byte[] Bytes);
}
