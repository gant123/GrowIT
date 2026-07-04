using GrowIT.Backend.Services;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrowIT.Backend.Controllers;

[Authorize(Policy = "AdminOrManager")]
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IReportManagementService _reports;

    public ReportsController(IReportManagementService reports)
    {
        _reports = reports;
    }

    [HttpGet("recent")]
    public async Task<ActionResult<List<RecentReport>>> GetRecentReports([FromQuery] RecentReportsQueryParams query)
    {
        return Ok(await _reports.GetRecentReportsAsync(query, HttpContext.RequestAborted));
    }

    [HttpGet("scheduled")]
    public async Task<ActionResult<List<ScheduledReport>>> GetScheduledReports([FromQuery] ScheduledReportsQueryParams query)
    {
        return Ok(await _reports.GetScheduledReportsAsync(query, HttpContext.RequestAborted));
    }

    [HttpPost("generate")]
    public async Task<ActionResult<RecentReport>> Generate([FromBody] GenerateReportRequest request)
    {
        return Ok(await _reports.GenerateAsync(request, HttpContext.RequestAborted));
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        try
        {
            var file = await _reports.DownloadAsync(
                id,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(),
                HttpContext.RequestAborted);

            return File(file.Bytes, file.ContentType, file.FileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException)
        {
            return Problem(
                title: "Report generation failed",
                detail: "The report could not be generated. Please review the run details and try again.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportRunDetailDto>> GetReportRun(Guid id)
    {
        var detail = await _reports.GetReportRunDetailAsync(id, HttpContext.RequestAborted);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost("scheduled")]
    public async Task<ActionResult<ScheduledReport>> CreateScheduled([FromBody] CreateScheduledReportRequest request)
    {
        return Ok(await _reports.CreateScheduledAsync(request, HttpContext.RequestAborted));
    }

    [HttpPut("scheduled/{id:guid}")]
    public async Task<ActionResult<ScheduledReport>> UpdateScheduled(Guid id, [FromBody] UpdateScheduledReportRequest request)
    {
        var updated = await _reports.UpdateScheduledAsync(id, request, HttpContext.RequestAborted);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("scheduled/{id:guid}")]
    public async Task<IActionResult> DeleteScheduled(Guid id)
    {
        await _reports.DeleteScheduledAsync(id, HttpContext.RequestAborted);
        return NoContent();
    }
}
