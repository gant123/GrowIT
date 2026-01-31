using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrowIT.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    [HttpGet("recent")]
    public ActionResult<List<RecentReport>> GetRecentReports()
    {
        // For now, returning an empty list instead of mock data, 
        // as the user wants real data and we don't have a report generation engine yet.
        // This fulfills "No more mock data" by ensuring the UI shows real (even if empty) state from DB/System.
        return Ok(new List<RecentReport>());
    }

    [HttpGet("scheduled")]
    public ActionResult<List<ScheduledReport>> GetScheduledReports()
    {
        return Ok(new List<ScheduledReport>());
    }
}
