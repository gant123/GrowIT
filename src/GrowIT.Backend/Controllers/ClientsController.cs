using System.Security.Claims;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Backend.Services;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums; // Required for ImpactOutcome
using InvestmentStatus = GrowIT.Shared.Enums.InvestmentStatus;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPlanLimitService _planLimits;

    public ClientsController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        IPlanLimitService planLimits)
    {
        _context = context;
        _tenantService = tenantService;
        _planLimits = planLimits;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ClientDetailDto>> GetClientDetail(Guid id)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Client not found.");

        // 1. Fetch Investments ($)
        var investments = await _context.Investments
            .Include(i => i.Fund)
            .Where(i => i.ClientId == id)
            .ToListAsync();

        // 2. Fetch Imprints / Milestones (Flags) - NEW
        var imprints = await _context.Imprints
            .Where(i => i.ClientId == id)
            .ToListAsync();

        // 3. Fetch Family Members
        var family = await _context.FamilyMembers
            .Where(f => f.ClientId == id)
            .ToListAsync();

        // 4. Build Unified Timeline (Merge Money + Milestones)
        var timeline = new List<TimelineItemDto>();

        // Add Investments
        timeline.AddRange(investments.Select(inv => new TimelineItemDto
        {
            Id = inv.Id,
            Date = inv.CreatedAt,
            Type = "Investment",
            Title = $"Investment: {inv.Fund?.Name ?? "General Fund"}",
            Description = inv.Reason,
            Amount = inv.Amount,
            Icon = "oi-dollar",
            ColorClass = "text-success"
        }));

        // Add Imprints (NEW)
        timeline.AddRange(imprints.Select(imp => new TimelineItemDto
        {
            Id = imp.Id,
            Date = imp.DateOccurred,
            Type = "Imprint",
            Title = imp.Title,
            Description = imp.Notes,
            Amount = null,
            Icon = "oi-flag",
            ColorClass = imp.Outcome == ImpactOutcome.Improved ? "text-primary" : "text-warning"
        }));

        var childFunding = investments
            .Where(i => i.FamilyMemberId.HasValue)
            .GroupBy(i => i.FamilyMemberId!.Value)
            .ToDictionary(g => g.Key, BuildFundingSummary);

        // 5. Construct Final DTO with Sorted Timeline
        var detail = new ClientDetailDto
        {
            Id = client.Id,
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = client.Email,
            Phone = client.Phone,
            Address = client.Address,
            DateOfBirth = client.DateOfBirth,
            MaskedSSNLast4 = MaskSsnLast4(client.SSNLast4),
            PhotoUrl = client.PhotoUrl,
            StabilityScore = client.StabilityScore,
            LifePhase = client.LifePhase,
            HouseholdCount = client.HouseholdCount,
            MaritalStatus = client.MaritalStatus,
            EmploymentStatus = client.EmploymentStatus,
            NextFollowupDate = client.NextFollowupDate,
            HouseholdId = client.HouseholdId,
            HouseholdRole = client.HouseholdRole,
            TotalInvestment = investments.Sum(x => x.Amount),
            LastInvestmentDate = investments.OrderByDescending(i => i.CreatedAt).FirstOrDefault()?.CreatedAt,
            
            // Sort merged timeline by Date DESC
            Timeline = timeline.OrderByDescending(t => t.Date).ToList(),

            HouseholdMembers = family.Select(f => new FamilyMemberDto
            {
                Id = f.Id,
                Name = $"{f.FirstName} {f.LastName}",
                Relationship = f.Relationship,
                Age = CalculateAge(f.DateOfBirth),
                School = f.SchoolOrEmployer,
                RequestedNeed = childFunding.TryGetValue(f.Id, out var funding) ? funding.RequestedNeed : 0m,
                FundedAmount = childFunding.TryGetValue(f.Id, out funding) ? funding.FundedAmount : 0m,
                RemainingNeed = childFunding.TryGetValue(f.Id, out funding) ? funding.RemainingNeed : 0m,
                LastSupportDate = childFunding.TryGetValue(f.Id, out funding) ? funding.LastSupportDate : null
            }).ToList()
        };

        return Ok(detail);
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> CreateClient(CreateClientRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        // Enforce the tenant's plan client limit (SuperAdmin is exempt).
        if (!User.IsSuperAdmin())
        {
            var usage = await _planLimits.GetUsageAsync();
            if (usage.AtClientLimit)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired,
                    $"Your {usage.PlanName} plan allows {usage.ClientsMax} clients ({usage.ClientsUsed} in use). Upgrade your plan to add more.");
            }
        }

        if (request.HouseholdId.HasValue)
        {
            var householdExists = await _context.Households
                .AnyAsync(h => h.Id == request.HouseholdId.Value && h.TenantId == tenantId.Value);

            if (!householdExists)
            {
                return BadRequest("Selected household was not found.");
            }
        }

        var newClient = new Client
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email?.Trim() ?? "",
            Phone = request.Phone?.Trim() ?? "",
            Address = request.Address.Trim(),
            DateOfBirth = ToUtcDate(request.DateOfBirth),
            SSNLast4 = NormalizeSsnLast4(request.SSNLast4),
            PhotoUrl = string.IsNullOrWhiteSpace(request.PhotoUrl) ? null : request.PhotoUrl.Trim(),
            MaritalStatus = request.MaritalStatus,
            EmploymentStatus = request.EmploymentStatus,
            HouseholdCount = request.HouseholdCount,
            StabilityScore = request.StabilityScore,
            LifePhase = request.LifePhase,
            HouseholdId = request.HouseholdId,
            HouseholdRole = request.HouseholdRole,
            NextFollowupDate = ToUtcDate(request.NextFollowupDate),
            TenantId = tenantId.Value
        };

        _context.Clients.Add(newClient);
        await _context.SaveChangesAsync();

        return Ok(new EntityCreatedResponse
        {
            Message = "Client Created",
            Id = newClient.Id,
            ClientId = newClient.Id
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientDto>>> GetAll([FromQuery] int take = 500)
    {
        var limit = Math.Clamp(take, 1, 1000);

        var clients = await _context.Clients
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Take(limit)
            .Select(c => new ClientDto
            {
                Id = c.Id,
                Name = c.FirstName + (string.IsNullOrEmpty(c.LastName) ? "" : " " + c.LastName),
                Email = c.Email,
                Phone = c.Phone,
                Address = c.Address,
                StabilityScore = c.StabilityScore,
                LifePhase = c.LifePhase,
                HouseholdCount = c.HouseholdCount,
                MaritalStatus = c.MaritalStatus,
                EmploymentStatus = c.EmploymentStatus,
                DateOfBirth = c.DateOfBirth,
                NextFollowupDate = c.NextFollowupDate
            })
            .ToListAsync();

        return Ok(clients);
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateClient(Guid id, CreateClientRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Client not found.");

        if (request.HouseholdId.HasValue)
        {
            var householdExists = await _context.Households
                .AnyAsync(h => h.Id == request.HouseholdId.Value && h.TenantId == tenantId.Value);

            if (!householdExists)
            {
                return BadRequest("Selected household was not found.");
            }
        }

        client.FirstName = request.FirstName.Trim();
        client.LastName = request.LastName.Trim();
        client.Email = request.Email?.Trim() ?? string.Empty;
        client.Phone = request.Phone?.Trim() ?? string.Empty;
        client.Address = request.Address.Trim();
        client.DateOfBirth = ToUtcDate(request.DateOfBirth);
        client.SSNLast4 = NormalizeSsnLast4(request.SSNLast4);
        client.PhotoUrl = string.IsNullOrWhiteSpace(request.PhotoUrl) ? null : request.PhotoUrl.Trim();
        client.MaritalStatus = request.MaritalStatus;
        client.EmploymentStatus = request.EmploymentStatus;
        client.HouseholdCount = request.HouseholdCount;
        client.StabilityScore = request.StabilityScore;
        client.LifePhase = request.LifePhase;
        client.HouseholdId = request.HouseholdId;
        client.HouseholdRole = request.HouseholdRole;
        client.NextFollowupDate = ToUtcDate(request.NextFollowupDate);

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id}/members")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> AddFamilyMember(Guid id, CreateFamilyMemberRequest request)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Client not found.");

        DateTime? utcBirthDate = null;
        if (request.DateOfBirth.HasValue)
        {
            utcBirthDate = DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc);
        }

        var member = new FamilyMember
        {
            Id = Guid.NewGuid(),
            TenantId = client.TenantId,
            ClientId = id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Relationship = request.Relationship,
            DateOfBirth = utcBirthDate,
            SchoolOrEmployer = request.SchoolOrEmployer,
            Notes = request.Notes
        };

        _context.FamilyMembers.Add(member);
        await _context.SaveChangesAsync();

        return Ok(new EntityCreatedResponse
        {
            Message = "Member Added",
            Id = member.Id
        });
    }

    // --- INDIVIDUAL MEMBER PROFILE (UPDATED) ---
    [HttpGet("members/{memberId}")]
    public async Task<ActionResult<FamilyMemberProfileDto>> GetMemberProfile(Guid memberId)
    {
        var member = await _context.FamilyMembers
            .Join(_context.Clients,
                member => member.ClientId,
                client => client.Id,
                (member, client) => new { member, client })
            .Where(x => x.member.Id == memberId)
            .Select(x => x.member)
            .FirstOrDefaultAsync();
        if (member == null) return NotFound();

        // 1. Fetch Investments for this person
        var investments = await _context.Investments
            .Include(i => i.Fund)
            .Where(i => i.FamilyMemberId == memberId)
            .ToListAsync();

        // 2. Fetch Imprints/Milestones for this person - NEW
        var imprints = await _context.Imprints
            .Where(i => i.FamilyMemberId == memberId)
            .ToListAsync();

        // 3. Build Unified Timeline
        var timeline = new List<TimelineItemDto>();

        timeline.AddRange(investments.Select(inv => new TimelineItemDto
        {
            Id = inv.Id,
            Date = inv.CreatedAt,
            Type = "Investment",
            Title = inv.Fund?.Name ?? "Individual Support",
            Description = inv.Reason,
            Amount = inv.Amount,
            Icon = "oi-dollar",
            ColorClass = "text-success"
        }));

        timeline.AddRange(imprints.Select(imp => new TimelineItemDto
        {
            Id = imp.Id,
            Date = imp.DateOccurred,
            Type = "Imprint",
            Title = imp.Title,
            Description = imp.Notes,
            Amount = null,
            Icon = "oi-flag",
            ColorClass = imp.Outcome == ImpactOutcome.Improved ? "text-primary" : "text-warning"
        }));

        var fundingSummary = BuildFundingSummary(investments);

        return Ok(new FamilyMemberProfileDto
        {
            Id = member.Id,
            ClientId = member.ClientId,
            Name = $"{member.FirstName} {member.LastName}",
            Relationship = member.Relationship,
            Age = CalculateAge(member.DateOfBirth),
            DateOfBirth = member.DateOfBirth,
            School = member.SchoolOrEmployer,
            Notes = member.Notes,
            TotalInvested = investments.Sum(i => i.Amount),
            RequestedNeed = fundingSummary.RequestedNeed,
            FundedAmount = fundingSummary.FundedAmount,
            RemainingNeed = fundingSummary.RemainingNeed,
            LastSupportDate = fundingSummary.LastSupportDate,
            
            // 4. Return the merged list sorted by date
            Timeline = timeline.OrderByDescending(t => t.Date).ToList()
        });
    }

    [HttpPut("members/{memberId}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateFamilyMember(Guid memberId, CreateFamilyMemberRequest request)
    {
        var member = await _context.FamilyMembers
            .Join(_context.Clients,
                member => member.ClientId,
                client => client.Id,
                (member, client) => new { member, client })
            .Where(x => x.member.Id == memberId)
            .Select(x => x.member)
            .FirstOrDefaultAsync();
        if (member == null) return NotFound();

        member.FirstName = request.FirstName;
        member.LastName = request.LastName;
        member.Relationship = request.Relationship;
        member.DateOfBirth = request.DateOfBirth.HasValue
            ? DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc)
            : null;
        member.SchoolOrEmployer = request.SchoolOrEmployer;
        member.Notes = request.Notes;

        await _context.SaveChangesAsync();
        return Ok(member);
    }

    [HttpDelete("members/{memberId}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteFamilyMember(Guid memberId)
    {
        var member = await _context.FamilyMembers
            .Join(_context.Clients,
                member => member.ClientId,
                client => client.Id,
                (member, client) => new { member, client })
            .Where(x => x.member.Id == memberId)
            .Select(x => x.member)
            .FirstOrDefaultAsync();
        if (member == null) return NotFound();

        // Optional: Check if they have investments before deleting
        var hasInvestments = await _context.Investments.AnyAsync(i => i.FamilyMemberId == memberId);
        if (hasInvestments)
        {
            return BadRequest("Cannot delete a member who has recorded investments. Reassign the investments first.");
        }

        var hasImprints = await _context.Imprints.AnyAsync(i => i.FamilyMemberId == memberId);
        if (hasImprints)
        {
            return BadRequest("Cannot delete a member who has recorded milestones. Reassign or remove the milestones first.");
        }

        _context.FamilyMembers.Remove(member);
        await _context.SaveChangesAsync();
        return Ok();
    }

    private static DateTime? ToUtcDate(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc);
    }

    private static int CalculateAge(DateTime? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
        {
            return 0;
        }

        var today = DateTime.UtcNow.Date;
        var birthDate = dateOfBirth.Value.Date;
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    private static string? MaskSsnLast4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var lastFour = value.Trim();
        return lastFour.Length == 4 ? $"***-**-{lastFour}" : "***-**-****";
    }

    private static string? NormalizeSsnLast4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 4 ? digits : null;
    }

    private static FundingSummary BuildFundingSummary(IEnumerable<Investment> investments)
    {
        var relevant = investments
            .Where(i => i.Status != InvestmentStatus.Cancelled && i.Status != InvestmentStatus.Returned)
            .ToList();

        var funded = relevant
            .Where(i => i.Status is InvestmentStatus.Approved or InvestmentStatus.Disbursed or InvestmentStatus.Completed)
            .ToList();

        var requestedNeed = relevant.Sum(i => i.Amount);
        var fundedAmount = funded.Sum(i => i.Amount);

        return new FundingSummary(
            requestedNeed,
            fundedAmount,
            Math.Max(0m, requestedNeed - fundedAmount),
            funded.OrderByDescending(i => i.CreatedAt).FirstOrDefault()?.CreatedAt);
    }

    private readonly record struct FundingSummary(
        decimal RequestedNeed,
        decimal FundedAmount,
        decimal RemainingNeed,
        DateTime? LastSupportDate);
}
