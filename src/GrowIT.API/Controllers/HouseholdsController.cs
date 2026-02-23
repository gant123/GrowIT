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
    public async Task<IActionResult> CreateHousehold(CreateHouseholdRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var household = new Household
        {
            Name = request.Name,
            PrimaryClientId = request.PrimaryClientId,
            TenantId = tenantId.Value
        };

        _context.Households.Add(household);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Household Created", HouseholdId = household.Id });
    }

    // 2. Get All Families (With Members!)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var households = await _context.Households
            .Include(h => h.Members) // This pulls in the Clients automatically
            .ToListAsync();

        return Ok(households);
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
