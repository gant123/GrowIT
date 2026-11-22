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
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        IPlanLimitService planLimits,
        ICurrentUserService currentUser,
        ILogger<ClientsController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _planLimits = planLimits;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ClientDetailDto>> GetClientDetail(Guid id)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Person not found.");

        var householdName = client.HouseholdId.HasValue
            ? await _context.Households
                .Where(h => h.Id == client.HouseholdId.Value)
                .Select(h => h.Name)
                .FirstOrDefaultAsync()
            : null;

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
            Title = $"Service record: {inv.Fund?.Name ?? "no money source"}",
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
            HouseholdName = householdName,
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

        Household? household = null;
        if (request.HouseholdId.HasValue)
        {
            household = await _context.Households
                .FirstOrDefaultAsync(h => h.Id == request.HouseholdId.Value && h.TenantId == tenantId.Value);

            if (household == null)
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
            HouseholdRole = request.HouseholdRole,
            NextFollowupDate = ToUtcDate(request.NextFollowupDate),
            TenantId = tenantId.Value
        };

        _context.Clients.Add(newClient);
        if (household != null)
        {
            await HouseholdMembership.AssignAsync(_context, newClient, household, request.HouseholdRole);
        }

        // A next follow-up date shows up in Follow-ups straight away (same save, so a person is
        // never created without the follow-up their date promises).
        await PersonFollowUps.SyncAsync(
            _context,
            newClient,
            previousDate: null,
            previousDefaultNotes: string.Empty,
            _currentUser.UserId);

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
        if (client == null) return NotFound("Person not found.");

        Household? household = null;
        if (request.HouseholdId.HasValue)
        {
            household = await _context.Households
                .FirstOrDefaultAsync(h => h.Id == request.HouseholdId.Value && h.TenantId == tenantId.Value);

            if (household == null)
            {
                return BadRequest("Selected household was not found.");
            }
        }

        var previousFollowupDate = client.NextFollowupDate;
        var previousDefaultNotes = PersonFollowUps.DefaultNotes(client.FirstName, client.LastName);

        client.FirstName = request.FirstName.Trim();
        client.LastName = request.LastName.Trim();
        client.Email = request.Email?.Trim() ?? string.Empty;
        client.Phone = request.Phone?.Trim() ?? string.Empty;
        client.Address = request.Address.Trim();
        client.DateOfBirth = ToUtcDate(request.DateOfBirth);
        // The stored digits are never returned to the browser, so an edit form cannot echo them
        // back. A blank value on update therefore means "unchanged", not "erase".
        if (!string.IsNullOrWhiteSpace(request.SSNLast4))
        {
            client.SSNLast4 = NormalizeSsnLast4(request.SSNLast4);
        }
        client.PhotoUrl = string.IsNullOrWhiteSpace(request.PhotoUrl) ? null : request.PhotoUrl.Trim();
        client.MaritalStatus = request.MaritalStatus;
        client.EmploymentStatus = request.EmploymentStatus;
        client.HouseholdCount = request.HouseholdCount;
        client.StabilityScore = request.StabilityScore;
        client.LifePhase = request.LifePhase;
        client.NextFollowupDate = ToUtcDate(request.NextFollowupDate);

        // Keep the household's head in agreement with this person's household and role.
        if (household != null)
        {
            if (client.HouseholdId != household.Id || client.HouseholdRole != request.HouseholdRole)
            {
                await HouseholdMembership.AssignAsync(_context, client, household, request.HouseholdRole);
            }
        }
        else
        {
            if (client.HouseholdId.HasValue)
            {
                await HouseholdMembership.UnlinkAsync(_context, client);
            }

            client.HouseholdRole = request.HouseholdRole;
        }

        await _context.SaveChangesAsync();

        // Move, add or cancel the follow-up that comes from the next follow-up date. Saved
        // separately so a double-submitted edit (both requests replacing the same follow-up)
        // cannot fail the profile save itself; the deterministic follow-up id means the
        // loser of that race has nothing left to do.
        try
        {
            await PersonFollowUps.SyncAsync(
                _context,
                client,
                previousFollowupDate,
                previousDefaultNotes,
                _currentUser.UserId);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Follow-up for person {ClientId} was already updated by a concurrent save.", client.Id);
        }

        return Ok();
    }

    // Deleting a person is only allowed while nothing else points at them: service records,
    // outcomes, growth plans and documents are the organization's history and must not vanish
    // (or be cascaded away) because a profile was removed. What goes with the person: their own
    // follow-ups, the family members recorded on their profile, and their place in a household.
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteClient(Guid id)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Person not found.");

        var familyMembers = await _context.FamilyMembers
            .Where(f => f.ClientId == id)
            .ToListAsync();
        var familyIds = familyMembers.Select(f => f.Id).ToList();

        var blocked = new ClientDeleteBlockedDto
        {
            ServiceRecords = await _context.Investments.CountAsync(i =>
                i.ClientId == id || (i.FamilyMemberId.HasValue && familyIds.Contains(i.FamilyMemberId.Value))),
            Outcomes = await _context.Imprints.CountAsync(i =>
                i.ClientId == id || (i.FamilyMemberId.HasValue && familyIds.Contains(i.FamilyMemberId.Value))),
            GrowthPlans = await _context.GrowthPlans.CountAsync(g =>
                g.ClientId == id || (g.FamilyMemberId.HasValue && familyIds.Contains(g.FamilyMemberId.Value))),
            Documents = await _context.Documents.CountAsync(d => d.ClientId == id)
        };

        var name = $"{client.FirstName} {client.LastName}".Trim();
        var linked = DescribeLinkedRecords(blocked);
        if (linked.Count > 0)
        {
            blocked.Message =
                $"{name} can't be deleted because {JoinWithAnd(linked)} {(IsSingular(blocked) ? "is" : "are")} linked to their profile. " +
                "Delete or move those records first, or keep this profile.";
            return Conflict(blocked);
        }

        var followUps = await _context.Tasks
            .Where(t => t.ClientId == id)
            .ToListAsync();

        if (client.HouseholdId.HasValue)
        {
            await HouseholdMembership.UnlinkAsync(_context, client);
        }

        // Also covers stale data where they are recorded as head of a household they left.
        var headedHouseholds = await _context.Households
            .Where(h => h.PrimaryClientId == id)
            .ToListAsync();
        foreach (var household in headedHouseholds)
        {
            household.PrimaryClientId = null;
        }

        _context.Tasks.RemoveRange(followUps);
        _context.FamilyMembers.RemoveRange(familyMembers);
        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();

        return Ok(new ClientDeleteResultDto
        {
            Message = $"Deleted {name}.",
            FollowUpsRemoved = followUps.Count,
            FamilyMembersRemoved = familyMembers.Count
        });
    }

    private static List<string> DescribeLinkedRecords(ClientDeleteBlockedDto counts)
    {
        var parts = new List<string>();
        if (counts.ServiceRecords > 0) parts.Add(Plural(counts.ServiceRecords, "service record", "service records"));
        if (counts.Outcomes > 0) parts.Add(Plural(counts.Outcomes, "outcome", "outcomes"));
        if (counts.GrowthPlans > 0) parts.Add(Plural(counts.GrowthPlans, "growth plan", "growth plans"));
        if (counts.Documents > 0) parts.Add(Plural(counts.Documents, "document", "documents"));
        return parts;
    }

    private static bool IsSingular(ClientDeleteBlockedDto counts) =>
        counts.ServiceRecords + counts.Outcomes + counts.GrowthPlans + counts.Documents == 1;

    private static string Plural(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count} {many}";

    private static string JoinWithAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => string.Empty,
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]
    };

    [HttpPost("{id}/members")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> AddFamilyMember(Guid id, CreateFamilyMemberRequest request)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id);
        if (client == null) return NotFound("Person not found.");

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
            return BadRequest("This family member has service records, so they can't be deleted. Move those records to another person first.");
        }

        var hasImprints = await _context.Imprints.AnyAsync(i => i.FamilyMemberId == memberId);
        if (hasImprints)
        {
            return BadRequest("This family member has outcomes recorded, so they can't be deleted. Move or delete those outcomes first.");
        }

        // Growth plans reference the family member with a foreign key, so deleting would fail.
        var hasGrowthPlans = await _context.GrowthPlans.AnyAsync(g => g.FamilyMemberId == memberId);
        if (hasGrowthPlans)
        {
            return BadRequest("This family member has a growth plan, so they can't be deleted. Delete that growth plan first.");
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
