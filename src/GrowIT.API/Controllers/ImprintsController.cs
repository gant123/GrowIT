
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;


namespace GrowIT.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImprintsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public ImprintsController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateImprint(CreateImprintRequest request)
    {
        // 1. Validate: Does the Investment exist?
        var investment = await _context.Investments
            .FirstOrDefaultAsync(i => i.Id == request.InvestmentId);

        if (investment == null) 
            return NotFound("Investment not found. Cannot create an imprint for a missing transaction.");

        // 2. Create the Imprint
        var imprint = new Imprint
        {
            InvestmentId = request.InvestmentId,
            Outcome = (ImpactOutcome)request.Outcome,
            Notes = request.Notes,
            FollowupDate = request.FollowupDate,
            TenantId = _tenantService.TenantId ?? Guid.Empty
        };

        // 3. Save
        _context.Imprints.Add(imprint);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Impact Recorded", ImprintId = imprint.Id });
    }

    [HttpGet("by-client/{clientId}")]
    public async Task<IActionResult> GetImprintsForClient(Guid clientId)
    {
        // Professional Query: Get all imprints for a specific person
        // We join Imprint -> Investment -> Client
        var imprints = await _context.Imprints
            .Include(i => i.Investment) // Load the investment details
            .Where(i => i.Investment.ClientId == clientId)
            .ToListAsync();

        return Ok(imprints);
    }
}