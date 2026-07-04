using GrowIT.Backend.Services;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/organization-readiness")]
public class OrganizationReadinessController : ControllerBase
{
    private readonly IAscScoreService _ascScoreService;

    public OrganizationReadinessController(IAscScoreService ascScoreService)
    {
        _ascScoreService = ascScoreService;
    }

    [HttpGet("asc-score")]
    public async Task<ActionResult<AscScoreDto>> GetAscScore(CancellationToken cancellationToken)
    {
        var score = await _ascScoreService.GetScoreAsync(cancellationToken);
        return Ok(score);
    }
}
