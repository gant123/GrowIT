using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class HouseholdsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public HouseholdsController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    // 1. Create a Family
    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<CreateHouseholdResponseDto>> CreateHousehold(CreateHouseholdRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        Client? primaryClient = null;
        if (request.PrimaryClientId.HasValue)
        {
            primaryClient = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.PrimaryClientId.Value);
            if (primaryClient == null)
            {
                return BadRequest("Primary client not found.");
            }
        }

        var household = new Household
        {
            Name = request.Name,
            PrimaryClientId = request.PrimaryClientId,
            TenantId = tenantId.Value
        };

        _context.Households.Add(household);
        await _context.SaveChangesAsync();

        // Keep the relationship consistent if a primary client was specified at creation time.
        if (primaryClient != null)
        {
            primaryClient.HouseholdId = household.Id;
            primaryClient.HouseholdRole = HouseholdRole.Head;
            await _context.SaveChangesAsync();
        }

        return Ok(new CreateHouseholdResponseDto
        {
            Message = "Household Created",
            HouseholdId = household.Id
        });
    }

    // 2. Get All Families (With Members!)
    [HttpGet]
    public async Task<ActionResult<List<HouseholdDto>>> GetAll()
    {
        var households = await _context.Households
            .Include(h => h.Members) // This pulls in the Clients automatically
            .ToListAsync();

        // Also pull the intake family members (spouse/children) recorded against each
        // household's clients, so the household view matches what shows on a case file.
        var clientIds = households.SelectMany(h => h.Members.Select(m => m.Id)).Distinct().ToList();
        var familyByClient = (await _context.FamilyMembers
                .Where(f => clientIds.Contains(f.ClientId))
                .ToListAsync())
            .GroupBy(f => f.ClientId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = households
            .Select(h =>
            {
                var primary = h.Members.FirstOrDefault(m => m.Id == h.PrimaryClientId);

                var clientMembers = h.Members.Select(m => new HouseholdMemberSummaryDto
                {
                    ClientId = m.Id,
                    Name = $"{m.FirstName} {m.LastName}".Trim(),
                    Role = m.HouseholdRole,
                    Email = m.Email,
                    IsCaseFile = true
                });

                var familyMembers = h.Members
                    .SelectMany(m => familyByClient.TryGetValue(m.Id, out var fam) ? fam : Enumerable.Empty<FamilyMember>())
                    .Select(f => new HouseholdMemberSummaryDto
                    {
                        ClientId = Guid.Empty,
                        Name = $"{f.FirstName} {f.LastName}".Trim(),
                        Role = MapRelationshipToRole(f.Relationship),
                        Email = string.Empty,
                        IsCaseFile = false
                    });

                var members = clientMembers
                    .Concat(familyMembers)
                    .OrderBy(m => m.Role)
                    .ThenBy(m => m.Name)
                    .ToList();

                return new HouseholdDto
                {
                    Id = h.Id,
                    Name = h.Name,
                    PrimaryClientId = h.PrimaryClientId,
                    PrimaryClientName = primary != null ? $"{primary.FirstName} {primary.LastName}".Trim() : null,
                    MemberCount = members.Count,
                    Members = members
                };
            })
            .OrderBy(h => h.Name)
            .ToList();

        return Ok(items);
    }

    // Maps a free-text intake relationship onto the household role used for display.
    private static HouseholdRole MapRelationshipToRole(string? relationship)
    {
        var rel = relationship?.Trim().ToLowerInvariant() ?? string.Empty;
        if (rel.Contains("spouse") || rel.Contains("wife") || rel.Contains("husband") || rel.Contains("partner"))
            return HouseholdRole.Spouse;
        if (rel.Contains("child") || rel.Contains("son") || rel.Contains("daughter") || rel.Contains("dependent"))
            return HouseholdRole.Dependent;
        return HouseholdRole.Other;
    }

    // 3. Add a Person to a Family
    [HttpPost("{householdId}/add-member/{clientId}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> AddMember(Guid householdId, Guid clientId, [FromQuery] HouseholdRole role)
    {
        var household = await _context.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household == null) return NotFound("Household not found");

        // Find the person (tenant-filtered)
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client == null) return NotFound("Client not found");

        // Link them
        client.HouseholdId = householdId;
        client.HouseholdRole = role;

        // If this is the "Head", update the Household record too
        if (role == HouseholdRole.Head)
        {
            household.PrimaryClientId = clientId;
        }

        await _context.SaveChangesAsync();
        return Ok(new MessageResponse { Message = $"Added {client.FirstName} to the household." });
    }

}
