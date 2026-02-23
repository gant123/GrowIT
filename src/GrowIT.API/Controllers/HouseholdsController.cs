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

        var items = households
            .Select(h =>
            {
                var primary = h.Members.FirstOrDefault(m => m.Id == h.PrimaryClientId);
                return new HouseholdDto
                {
                    Id = h.Id,
                    Name = h.Name,
                    PrimaryClientId = h.PrimaryClientId,
                    PrimaryClientName = primary != null ? $"{primary.FirstName} {primary.LastName}".Trim() : null,
                    MemberCount = h.Members.Count,
                    Members = h.Members
                        .OrderBy(m => m.FirstName)
                        .ThenBy(m => m.LastName)
                        .Select(m => new HouseholdMemberSummaryDto
                        {
                            ClientId = m.Id,
                            Name = $"{m.FirstName} {m.LastName}".Trim(),
                            Role = m.HouseholdRole,
                            Email = m.Email
                        })
                        .ToList()
                };
            })
            .OrderBy(h => h.Name)
            .ToList();

        return Ok(items);
    }

    // 3. Add a Person to a Family
    [HttpPost("{householdId}/add-member/{clientId}")]
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
        return Ok(new { Message = $"Added {client.FirstName} to the household." });
    }

}
