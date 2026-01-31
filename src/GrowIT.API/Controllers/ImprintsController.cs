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
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        // 1. Validation & Integrity Checks
        
        // Verify Client exists and belongs to tenant
        var client = await _context.Clients
            .AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId);
        if (!client)
            return BadRequest("Invalid ClientId or Client does not belong to this tenant.");

        // Verify FamilyMember exists and belongs to that client (and by extension tenant)
        if (request.FamilyMemberId.HasValue)
        {
            var familyMemberExists = await _context.FamilyMembers
                .AnyAsync(f => f.Id == request.FamilyMemberId && f.ClientId == request.ClientId);
            
            if (!familyMemberExists)
                return BadRequest("FamilyMember not found or does not belong to this client.");
        }

        // If they linked an investment, make sure it exists and belongs to the same client/tenant
        if (request.InvestmentId.HasValue)
        {
            var investmentExists = await _context.Investments
                .AnyAsync(i => i.Id == request.InvestmentId && i.ClientId == request.ClientId && i.TenantId == tenantId);
            
            if (!investmentExists) 
                return NotFound("Linked Investment not found or belongs to another client.");
        }

        // 2. Create the Milestone (Imprint)
        var imprint = new Imprint
        {
            ClientId = request.ClientId,       // Required: Links to the Family
            FamilyMemberId = request.FamilyMemberId, // Optional: Links to specific person
            InvestmentId = request.InvestmentId, // Optional
            
            Title = request.Title,
            Category = request.Category,
            Outcome = request.Outcome,
            Notes = request.Notes,
            
            // Ensure Dates are UTC for Postgres
            DateOccurred = DateTime.SpecifyKind(request.DateOccurred, DateTimeKind.Utc),
            FollowupDate = request.FollowupDate.HasValue 
                ? DateTime.SpecifyKind(request.FollowupDate.Value, DateTimeKind.Utc) 
                : null,
            
            TenantId = tenantId.Value
        };

        // 3. Save
        _context.Imprints.Add(imprint);
        await _context.SaveChangesAsync();

        var response = new ImprintResponseDto
        {
            Id = imprint.Id,
            ClientId = imprint.ClientId,
            FamilyMemberId = imprint.FamilyMemberId,
            InvestmentId = imprint.InvestmentId,
            Title = imprint.Title,
            DateOccurred = imprint.DateOccurred,
            Category = imprint.Category,
            Outcome = imprint.Outcome,
            Notes = imprint.Notes,
            FollowupDate = imprint.FollowupDate
        };

        return CreatedAtAction(nameof(GetById), new { id = imprint.Id }, response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ImprintResponseDto>> GetById(Guid id)
    {
        var tenantId = _tenantService.TenantId;
        var imprint = await _context.Imprints
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Select(i => new ImprintResponseDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                FamilyMemberId = i.FamilyMemberId,
                InvestmentId = i.InvestmentId,
                Title = i.Title,
                DateOccurred = i.DateOccurred,
                Category = i.Category,
                Outcome = i.Outcome,
                Notes = i.Notes,
                FollowupDate = i.FollowupDate
            })
            .FirstOrDefaultAsync(i => i.Id == id);

        if (imprint == null) return NotFound();

        return Ok(imprint);
    }

    [HttpGet("member/{memberId}")]
    public async Task<ActionResult<List<ImprintResponseDto>>> GetImprintsForMember(Guid memberId)
    {
        var tenantId = _tenantService.TenantId;
        
        // Fetch all milestones for a specific family member, enforced by tenant
        var imprints = await _context.Imprints
            .AsNoTracking()
            .Where(i => i.FamilyMemberId == memberId && i.TenantId == tenantId)
            .OrderByDescending(i => i.DateOccurred)
            .Select(i => new ImprintResponseDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                FamilyMemberId = i.FamilyMemberId,
                InvestmentId = i.InvestmentId,
                Title = i.Title,
                DateOccurred = i.DateOccurred,
                Category = i.Category,
                Outcome = i.Outcome,
                Notes = i.Notes,
                FollowupDate = i.FollowupDate
            })
            .ToListAsync();

        return Ok(imprints);
    }

    [HttpGet]
    public async Task<ActionResult<List<ImprintListDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = _tenantService.TenantId;
        
        var imprints = await _context.Imprints
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Include(i => i.Client)
            .OrderByDescending(i => i.DateOccurred)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ImprintListDto
            {
                Id = i.Id,
                PersonId = i.FamilyMemberId ?? i.ClientId,
                PersonName = i.FamilyMember != null 
                    ? (i.FamilyMember.FirstName + " " + i.FamilyMember.LastName)
                    : (i.Client != null ? (i.Client.FirstName + " " + i.Client.LastName) : "Unknown"),
                Title = i.Title,
                Category = i.Category,
                Date = i.DateOccurred,
                IsVerified = true 
            })
            .ToListAsync();

        return Ok(imprints);
    }
}