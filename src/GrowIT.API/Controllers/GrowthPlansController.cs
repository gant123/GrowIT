using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GrowthPlansController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GrowthPlansController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<ActionResult<List<GrowthPlanListDto>>> GetAll()
    {
        var entities = await _context.GrowthPlans
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.FamilyMember)
            .Include(x => x.AssignedToUser)
            .OrderByDescending(x => x.StartDate)
            .ToListAsync();

        var items = entities.Select(ToDto).ToList();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GrowthPlanListDto>> GetById(Guid id)
    {
        var item = await _context.GrowthPlans
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.FamilyMember)
            .Include(x => x.AssignedToUser)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return NotFound();

        return Ok(ToDto(item));
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<GrowthPlanListDto>> Create([FromBody] CreateGrowthPlanRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        if (request.PersonId == Guid.Empty) return BadRequest("PersonId is required.");
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Title is required.");

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.PersonId);
        FamilyMember? familyMember = null;

        if (client == null)
        {
            familyMember = await _context.FamilyMembers.FirstOrDefaultAsync(f => f.Id == request.PersonId);
            if (familyMember == null)
                return BadRequest("PersonId must match a client or family member in this tenant.");

            client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == familyMember.ClientId);
            if (client == null)
                return BadRequest("Family member is missing a valid parent client.");
        }

        var item = new GrowthPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            ClientId = client.Id,
            FamilyMemberId = familyMember?.Id,
            Title = request.Title.Trim(),
            Season = request.Season,
            Status = GrowthPlanStatus.Active,
            StartDate = request.StartDate == default ? DateTime.UtcNow : request.StartDate,
            TargetEndDate = request.TargetEndDate,
            CompletedGoals = 0,
            TotalGoals = 0,
        };

        _context.GrowthPlans.Add(item);
        await _context.SaveChangesAsync();

        item.Client = client;
        item.FamilyMember = familyMember;

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, ToDto(item));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<GrowthPlanListDto>> Update(Guid id, [FromBody] UpdateGrowthPlanRequest request)
    {
        var existing = await _context.GrowthPlans
            .Include(x => x.Client)
            .Include(x => x.FamilyMember)
            .Include(x => x.AssignedToUser)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (existing == null)
            return NotFound();

        existing.Title = string.IsNullOrWhiteSpace(request.Title) ? existing.Title : request.Title.Trim();
        existing.Season = request.Season;
        existing.Status = request.Status;
        existing.TargetEndDate = request.TargetEndDate;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ToDto(existing));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var existing = await _context.GrowthPlans.FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null)
            return NoContent();

        _context.GrowthPlans.Remove(existing);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private static GrowthPlanListDto ToDto(GrowthPlan entity)
    {
        var personId = entity.FamilyMemberId ?? entity.ClientId;
        var personName = entity.FamilyMember != null
            ? $"{entity.FamilyMember.FirstName} {entity.FamilyMember.LastName}".Trim()
            : (entity.Client != null ? $"{entity.Client.FirstName} {entity.Client.LastName}".Trim() : "Unknown");

        var progress = entity.TotalGoals <= 0
            ? 0m
            : decimal.Round((decimal)entity.CompletedGoals / entity.TotalGoals * 100m, 2);

        return new GrowthPlanListDto
        {
            Id = entity.Id,
            PersonId = personId,
            PersonName = personName,
            Title = entity.Title,
            Season = entity.Season,
            Status = entity.Status,
            StartDate = entity.StartDate,
            TargetEndDate = entity.TargetEndDate,
            CompletedGoals = entity.CompletedGoals,
            TotalGoals = entity.TotalGoals,
            ProgressPercentage = progress,
            AssignedToUserName = entity.AssignedToUser != null
                ? $"{entity.AssignedToUser.FirstName} {entity.AssignedToUser.LastName}".Trim()
                : null
        };
    }
}
