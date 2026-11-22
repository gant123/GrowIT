using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

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

        if (request.PersonId == Guid.Empty) return BadRequest("Choose the person this plan is for.");
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("Give the plan a title.");

        var startDate = request.StartDate == default ? DateTime.UtcNow : ToUtc(request.StartDate);
        var targetEndDate = ToUtc(request.TargetEndDate);
        if (targetEndDate.HasValue && targetEndDate.Value.Date < startDate.Date)
            return BadRequest("The target end date can't be before the start date.");

        var ownerError = await ValidateOwnerAsync(request.AssignedToUserId, tenantId.Value);
        if (ownerError is not null) return BadRequest(ownerError);

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.PersonId);
        FamilyMember? familyMember = null;

        if (client == null)
        {
            familyMember = await _context.FamilyMembers.FirstOrDefaultAsync(f => f.Id == request.PersonId);
            if (familyMember == null)
                return BadRequest("Choose a person from your organization.");

            client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == familyMember.ClientId);
            if (client == null)
                return BadRequest("That household member isn't linked to a person in your organization.");
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
            StartDate = startDate,
            TargetEndDate = targetEndDate,
            AssignedToUserId = request.AssignedToUserId,
            CompletedGoals = 0,
            TotalGoals = request.TotalGoals,
        };

        _context.GrowthPlans.Add(item);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = item.Id }, await FindDtoAsync(item.Id));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<GrowthPlanListDto>> Update(Guid id, [FromBody] UpdateGrowthPlanRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        var existing = await _context.GrowthPlans.FirstOrDefaultAsync(x => x.Id == id);

        if (existing == null)
            return NotFound();

        var targetEndDate = ToUtc(request.TargetEndDate);
        if (targetEndDate.HasValue && targetEndDate.Value.Date < existing.StartDate.Date)
            return BadRequest("The target end date can't be before the start date.");

        if (request.AssignedToUserId != existing.AssignedToUserId)
        {
            var ownerError = await ValidateOwnerAsync(request.AssignedToUserId, tenantId.Value);
            if (ownerError is not null) return BadRequest(ownerError);
        }

        var goalsError = ApplyGoals(existing, request.TotalGoals, request.CompletedGoals);
        if (goalsError is not null) return BadRequest(goalsError);

        existing.Title = string.IsNullOrWhiteSpace(request.Title) ? existing.Title : request.Title.Trim();
        existing.Season = request.Season;
        existing.Status = request.Status;
        existing.TargetEndDate = targetEndDate;
        existing.AssignedToUserId = request.AssignedToUserId;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(await FindDtoAsync(existing.Id));
    }

    // Record progress (goals completed, and optionally the number of goals) from the plan card.
    [HttpPatch("{id:guid}/progress")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<GrowthPlanListDto>> UpdateProgress(Guid id, [FromBody] UpdateGrowthPlanProgressRequest request)
    {
        var existing = await _context.GrowthPlans.FirstOrDefaultAsync(x => x.Id == id);
        if (existing == null)
            return NotFound();

        var goalsError = ApplyGoals(existing, request.TotalGoals, request.CompletedGoals);
        if (goalsError is not null) return BadRequest(goalsError);

        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await FindDtoAsync(existing.Id));
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

    private static string? ApplyGoals(GrowthPlan plan, int? totalGoals, int? completedGoals)
    {
        var total = totalGoals ?? plan.TotalGoals;
        var completed = completedGoals ?? plan.CompletedGoals;

        if (total < 0 || total > GrowthPlanLimits.MaxGoals)
            return $"Use between 0 and {GrowthPlanLimits.MaxGoals} goals.";
        if (completed < 0)
            return "Goals completed can't be negative.";
        if (completed > total)
            return total == 0
                ? "Add the number of goals before recording goals completed."
                : $"Goals completed can't be more than the plan's {total} goal(s).";

        plan.TotalGoals = total;
        plan.CompletedGoals = completed;
        return null;
    }

    // The owner must be an active team member of this organization (same rule as follow-up assignees).
    private async Task<string?> ValidateOwnerAsync(Guid? userId, Guid tenantId)
    {
        if (!userId.HasValue) return null;

        var isActiveMember = await _context.Users
            .AnyAsync(u => u.Id == userId.Value && u.TenantId == tenantId && u.IsActive);
        return isActiveMember ? null : "Choose an active team member as the plan owner.";
    }

    private async Task<GrowthPlanListDto?> FindDtoAsync(Guid id)
    {
        var item = await _context.GrowthPlans
            .AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.FamilyMember)
            .Include(x => x.AssignedToUser)
            .FirstOrDefaultAsync(x => x.Id == id);

        return item == null ? null : ToDto(item);
    }

    // Dates arrive from the browser date picker with Kind=Unspecified; Npgsql rejects
    // anything but Kind=Utc for timestamptz columns.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;

    private static string OwnerName(User user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? (user.Email ?? "Team member") : name;
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
            AssignedToUserId = entity.AssignedToUserId,
            AssignedToUserName = entity.AssignedToUser != null
                ? OwnerName(entity.AssignedToUser)
                : null
        };
    }
}
