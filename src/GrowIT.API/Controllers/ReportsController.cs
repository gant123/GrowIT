using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public async Task<ActionResult<List<RecentReport>>> GetRecentReports()
    {
        var items = await _context.ReportRuns
            .AsNoTracking()
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new RecentReport
            {
                Id = r.Id,
                Name = r.Name,
                Format = r.Format,
                GeneratedAt = r.GeneratedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("scheduled")]
    public async Task<ActionResult<List<ScheduledReport>>> GetScheduledReports()
    {
        var items = await _context.ReportSchedules
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.NextRun)
            .Select(s => new ScheduledReport
            {
                Id = s.Id,
                Name = s.Name,
                Frequency = s.Frequency,
                NextRun = s.NextRun
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
            RequestedByUserId = _currentUserService.UserId,
            GeneratedAt = DateTime.UtcNow
        };

        _context.ReportRuns.Add(run);
        await _context.SaveChangesAsync();

        return Ok(new RecentReport
        {
            Id = run.Id,
            Name = run.Name,
            Format = run.Format,
            GeneratedAt = run.GeneratedAt
        });
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var report = await _context.ReportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id);

        if (report == null)
        {
            return NotFound();
        }

        var content = $"GrowIT Report\nName: {report.Name}\nFormat: {report.Format}\nGeneratedAtUtc: {report.GeneratedAt:O}\n";
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileName = $"{SanitizeFileName(report.Name)}.{report.Format.ToLowerInvariant()}";
        return File(bytes, "application/octet-stream", fileName);
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
            Frequency = item.Frequency,
            NextRun = item.NextRun
        });
    }

    [HttpPut("scheduled/{id:guid}")]
    public async Task<ActionResult<ScheduledReport>> UpdateScheduled(Guid id, [FromBody] UpdateScheduledReportRequest request)
    {
        var existing = await _context.ReportSchedules.FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null)
            return NotFound();

        existing.Name = string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name.Trim();
        existing.Frequency = string.IsNullOrWhiteSpace(request.Frequency) ? existing.Frequency : request.Frequency.Trim();
        existing.NextRun = request.NextRun;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new ScheduledReport
        {
            Id = existing.Id,
            Name = existing.Name,
            Frequency = existing.Frequency,
            NextRun = existing.NextRun
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

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value;
    }
}
