
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;

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

    // 1. Fetch Investments
    var investments = await _context.Investments
        .Include(i => i.Fund)
        .Where(i => i.ClientId == id)
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync();

    // 2. *** THE MISSING PIECE: Fetch Family Members ***
    var family = await _context.FamilyMembers
        .Where(f => f.ClientId == id)
        .ToListAsync();

    // 3. Build Timeline
    var timeline = investments.Select(inv => new TimelineItemDto
    {
        Id = inv.Id,
        Date = inv.CreatedAt,
        Type = "Investment",
        Title = $"Investment: {inv.Fund?.Name ?? "General Fund"}",
        Description = inv.Reason,
        Amount = inv.Amount,
        Icon = "oi-dollar",
        ColorClass = "text-success"
    }).ToList();

    // 4. Construct Final DTO
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
        LastInvestmentDate = investments.FirstOrDefault()?.CreatedAt,
        Timeline = timeline,

        // *** MAP THE FAMILY MEMBERS HERE ***
        HouseholdMembers = family.Select(f => new FamilyMemberDto
        {
            Id = f.Id,
            Name = $"{f.FirstName} {f.LastName}",
            Relationship = f.Relationship,
            Age = f.Age, // Uses the [NotMapped] logic in your entity
            School = f.SchoolOrEmployer
        }).ToList()
    };

    return Ok(detail);
}
    
    [HttpPost]
    public async Task<IActionResult> CreateClient(CreateClientRequest request)
    {
        // 1. Map DTO -> Entity
        var newClient = new Client
        {
            // Map "Name" from form to "FirstName" in DB (Standard for Orgs)
            FirstName = request.Name,
            LastName = "", 
            
            Email = request.Email ?? "",
            Phone = request.Phone ?? "",
            
            HouseholdCount = request.HouseholdCount,
            StabilityScore = request.StabilityScore,
            LifePhase = request.LifePhase, // Works because we fixed the Enum in DTO
            
            TenantId = _tenantService.TenantId ?? Guid.Empty 
        };

        _context.Clients.Add(newClient);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Client Created", ClientId = newClient.Id });
    }

    [HttpGet]
    public async Task<ActionResult<List<ClientDto>>> GetAll()
    {
        // 2. Map Entity -> DTO (This requires the ClientDto class we just created)
        var clients = await _context.Clients
            .Select(c => new ClientDto 
            {
                Id = c.Id,
                // If LastName is empty, just show FirstName (Organization Name)
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

    // THE FIX: Ensure the DateOfBirth is treated as UTC before saving
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
        DateOfBirth = utcBirthDate, // Use the UTC version
        SchoolOrEmployer = request.SchoolOrEmployer,
        Notes = request.Notes
    };

    _context.FamilyMembers.Add(member);
    await _context.SaveChangesAsync();

    return Ok(new { Message = "Member Added", Id = member.Id });
}
[HttpGet("members/{memberId}")]
public async Task<ActionResult<FamilyMemberProfileDto>> GetMemberProfile(Guid memberId)
{
    var member = await _context.FamilyMembers.FindAsync(memberId);
    if (member == null) return NotFound();

    var investments = await _context.Investments
        .Include(i => i.Fund)
        .Where(i => i.FamilyMemberId == memberId)
        .OrderByDescending(i => i.CreatedAt)
        .ToListAsync();

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
        Timeline = investments.Select(inv => new TimelineItemDto
        {
            Id = inv.Id,
            Date = inv.CreatedAt,
            Type = "Investment",
            Title = inv.Fund?.Name ?? "Individual Support",
            Description = inv.Reason,
            Amount = inv.Amount,
            Icon = "oi-person",
            ColorClass = "text-info"
        }).ToList()
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

    // Safety Check: Are there investments linked to this person?
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