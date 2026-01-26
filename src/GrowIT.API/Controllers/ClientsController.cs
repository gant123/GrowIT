using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums; // Required for ImpactOutcome

namespace GrowIT.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public ClientsController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
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

        // 5. Construct Final DTO with Sorted Timeline
        var detail = new ClientDetailDto
        {
            Id = client.Id,
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = client.Email,
            Phone = client.Phone,
            Address = client.Address,
            StabilityScore = client.StabilityScore,
            LifePhase = client.LifePhase.ToString(),
            HouseholdCount = client.HouseholdCount,
            EmploymentStatus = client.EmploymentStatus.ToString(),
            TotalInvestment = investments.Sum(x => x.Amount),
            LastInvestmentDate = investments.OrderByDescending(i => i.CreatedAt).FirstOrDefault()?.CreatedAt,
            
            // Sort merged timeline by Date DESC
            Timeline = timeline.OrderByDescending(t => t.Date).ToList(),

            HouseholdMembers = family.Select(f => new FamilyMemberDto
            {
                Id = f.Id,
                Name = $"{f.FirstName} {f.LastName}",
                Relationship = f.Relationship,
                Age = f.Age,
                School = f.SchoolOrEmployer
            }).ToList()
        };

        return Ok(detail);
    }

    [HttpPost]
    public async Task<IActionResult> CreateClient(CreateClientRequest request)
    {
        var newClient = new Client
        {
            FirstName = request.Name,
            LastName = "",
            Email = request.Email ?? "",
            Phone = request.Phone ?? "",
            HouseholdCount = request.HouseholdCount,
            StabilityScore = request.StabilityScore,
            LifePhase = request.LifePhase,
            TenantId = _tenantService.TenantId ?? Guid.Empty
        };

        _context.Clients.Add(newClient);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Client Created", ClientId = newClient.Id });
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientDto>>> GetAll()
    {
        var clients = await _context.Clients
            .Select(c => new ClientDto
            {
                Id = c.Id,
                Name = c.FirstName + (string.IsNullOrEmpty(c.LastName) ? "" : " " + c.LastName),
                Email = c.Email,
                Phone = c.Phone,
                StabilityScore = c.StabilityScore,
                LifePhase = c.LifePhase.ToString()
            })
            .ToListAsync();

        return Ok(clients);
    }

    [HttpPost("{id}/members")]
    public async Task<IActionResult> AddFamilyMember(Guid id, CreateFamilyMemberRequest request)
    {
        var client = await _context.Clients.FindAsync(id);
        if (client == null) return NotFound("Client not found.");

        DateTime? utcBirthDate = null;
        if (request.DateOfBirth.HasValue)
        {
            utcBirthDate = DateTime.SpecifyKind(request.DateOfBirth.Value, DateTimeKind.Utc);
        }

        var member = new FamilyMember
        {
            Id = Guid.NewGuid(),
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

        return Ok(new { Message = "Member Added", Id = member.Id });
    }

    // --- INDIVIDUAL MEMBER PROFILE (UPDATED) ---
    [HttpGet("members/{memberId}")]
    public async Task<ActionResult<FamilyMemberProfileDto>> GetMemberProfile(Guid memberId)
    {
        var member = await _context.FamilyMembers.FindAsync(memberId);
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

        return Ok(new FamilyMemberProfileDto
        {
            Id = member.Id,
            Name = $"{member.FirstName} {member.LastName}",
            Relationship = member.Relationship,
            Age = member.Age,
            DateOfBirth = member.DateOfBirth,
            School = member.SchoolOrEmployer,
            Notes = member.Notes,
            TotalInvested = investments.Sum(i => i.Amount),
            
            // 4. Return the merged list sorted by date
            Timeline = timeline.OrderByDescending(t => t.Date).ToList()
        });
    }

    [HttpPut("members/{memberId}")]
    public async Task<IActionResult> UpdateFamilyMember(Guid memberId, CreateFamilyMemberRequest request)
    {
        var member = await _context.FamilyMembers.FindAsync(memberId);
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
    public async Task<IActionResult> DeleteFamilyMember(Guid memberId)
    {
        var member = await _context.FamilyMembers.FindAsync(memberId);
        if (member == null) return NotFound();

        // Optional: Check if they have investments before deleting
        var hasInvestments = await _context.Investments.AnyAsync(i => i.FamilyMemberId == memberId);
        if (hasInvestments)
        {
            return BadRequest("Cannot delete a member who has recorded investments. Reassign the investments first.");
        }

        _context.FamilyMembers.Remove(member);
        await _context.SaveChangesAsync();
        return Ok();
    }
}