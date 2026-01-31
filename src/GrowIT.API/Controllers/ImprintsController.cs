using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.API.Controllers;

[Authorize]
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
        // 1. Optional Validation: If they DID link an investment, make sure it exists.
        if (request.InvestmentId.HasValue)
        {
            var investmentExists = await _context.Investments
                .AnyAsync(i => i.Id == request.InvestmentId);
            
            if (!investmentExists) 
                return NotFound("Linked Investment not found.");
        }

        // 2. Create the Milestone (Imprint)
        var imprint = new Imprint
        {
            ClientId = request.ClientId,       // Required: Links to the Family
            FamilyMemberId = request.FamilyMemberId, // Optional: Links to specific person
            InvestmentId = request.InvestmentId, // Optional: Can be null now!
            
            Title = request.Title,
            Category = request.Category,
            Outcome = request.Outcome,
            Notes = request.Notes,
            
            // Ensure Dates are UTC for Postgres
            DateOccurred = DateTime.SpecifyKind(request.DateOccurred, DateTimeKind.Utc),
            FollowupDate = request.FollowupDate.HasValue 
                ? DateTime.SpecifyKind(request.FollowupDate.Value, DateTimeKind.Utc) 
                : null,
            
            TenantId = _tenantService.TenantId ?? Guid.Empty
        };

        // 3. Save
        _context.Imprints.Add(imprint);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Milestone Recorded", ImprintId = imprint.Id });
    }

    [HttpGet("member/{memberId}")]
    public async Task<IActionResult> GetImprintsForMember(Guid memberId)
    {
        // Fetch all milestones for a specific family member
        var imprints = await _context.Imprints
            .Where(i => i.FamilyMemberId == memberId)
            .OrderByDescending(i => i.DateOccurred)
            .ToListAsync();

        return Ok(imprints);
    }

    [HttpGet]
    public async Task<ActionResult<List<ImprintListDto>>> GetAll()
    {
        var imprints = await _context.Imprints
            .Include(i => i.Client)
            .OrderByDescending(i => i.DateOccurred)
            .Select(i => new ImprintListDto
            {
                Id = i.Id,
                PersonName = i.Client != null ? (i.Client.FirstName + " " + i.Client.LastName) : "Unknown",
                Title = i.Title,
                Category = i.Category,
                Date = i.DateOccurred,
                IsVerified = true // For now, all recorded imprints are considered verified
            })
            .ToListAsync();

        return Ok(imprints);
    }
}