using GrowIT.Backend.Services;
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

        Client? head = null;
        if (request.PrimaryClientId.HasValue)
        {
            head = await _context.Clients.FirstOrDefaultAsync(c => c.Id == request.PrimaryClientId.Value);
            if (head == null)
            {
                return BadRequest("The person chosen as head of household was not found.");
            }
        }

        var household = new Household
        {
            Name = request.Name.Trim(),
            TenantId = tenantId.Value
        };

        _context.Households.Add(household);

        // Link the head in the same save, and take them out of any household they headed before.
        if (head != null)
        {
            await HouseholdMembership.AssignAsync(_context, head, household, HouseholdRole.Head);
        }

        await _context.SaveChangesAsync();

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
        if (!Enum.IsDefined(role))
        {
            return BadRequest("Choose a household role.");
        }

        var household = await _context.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household == null) return NotFound("Household not found.");

        // Find the person (tenant-filtered)
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client == null) return NotFound("Person not found.");

        var alreadyMember = client.HouseholdId == householdId;

        // Keeps the household's head (PrimaryClientId) and the members' roles in agreement, and
        // clears the head of a household they are moving out of. Previously, re-adding the head
        // with another role left the card saying "Head: Maria" while her row said "Dependent".
        await HouseholdMembership.AssignAsync(_context, client, household, role);

        await _context.SaveChangesAsync();

        var message = alreadyMember
            ? $"{client.FirstName} is already in this household. Their role is now {FormatRole(role)}."
            : $"Added {client.FirstName} to {household.Name}.";
        return Ok(new MessageResponse { Message = message });
    }

    // 4. Rename a household
    [HttpPut("{householdId:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> RenameHousehold(Guid householdId, UpdateHouseholdRequest request)
    {
        var household = await _context.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household == null) return NotFound("Household not found.");

        household.Name = request.Name.Trim();
        await _context.SaveChangesAsync();

        return Ok(new MessageResponse { Message = $"Renamed the household to {household.Name}." });
    }

    // 5. Delete a household. The people in it are not deleted: they stay in People and are
    // simply no longer grouped (their household link and head flag are cleared).
    [HttpDelete("{householdId:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteHousehold(Guid householdId)
    {
        var household = await _context.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household == null) return NotFound("Household not found.");

        var members = await _context.Clients
            .Where(c => c.HouseholdId == householdId)
            .ToListAsync();

        foreach (var member in members)
        {
            member.HouseholdId = null;
            member.HouseholdRole = HouseholdMembership.UnlinkedRole;
        }

        _context.Households.Remove(household);
        await _context.SaveChangesAsync();

        var message = members.Count switch
        {
            0 => $"Deleted {household.Name}.",
            1 => $"Deleted {household.Name}. The 1 person in it is still in People.",
            _ => $"Deleted {household.Name}. The {members.Count} people in it are still in People."
        };
        return Ok(new MessageResponse { Message = message });
    }

    // 6. Take a person out of a household (they stay in People). If they were the head,
    // the household no longer has one.
    [HttpDelete("{householdId:guid}/members/{clientId:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> RemoveMember(Guid householdId, Guid clientId)
    {
        var household = await _context.Households.FirstOrDefaultAsync(h => h.Id == householdId);
        if (household == null) return NotFound("Household not found.");

        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == clientId);
        if (client == null || client.HouseholdId != householdId)
        {
            return NotFound("That person is not in this household.");
        }

        var wasHead = household.PrimaryClientId == clientId;
        await HouseholdMembership.UnlinkAsync(_context, client);
        await _context.SaveChangesAsync();

        var name = $"{client.FirstName} {client.LastName}".Trim();
        var message = wasHead
            ? $"Removed {name} from {household.Name}. The household has no head now."
            : $"Removed {name} from {household.Name}.";
        return Ok(new MessageResponse { Message = message });
    }

    private static string FormatRole(HouseholdRole role) => role switch
    {
        HouseholdRole.Head => "head of household",
        HouseholdRole.Spouse => "spouse",
        HouseholdRole.Dependent => "dependent",
        _ => "other"
    };

}
