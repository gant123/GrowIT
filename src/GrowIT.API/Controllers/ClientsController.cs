using GrowIT.API.DTOs;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;

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

    [HttpPost]
    public async Task<IActionResult> CreateClient(CreateClientRequest request)
    {
        // 1. Convert DTO to Entity
        var newClient = new Client
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email ?? "",
            Phone = request.Phone ?? "",
            HouseholdCount = request.HouseholdCount,
            StabilityScore = request.StabilityScore,
            LifePhase = request.LifePhase,
            HouseholdId = request.HouseholdId,
            
            // Auto-set the Tenant (Security)
            TenantId = _tenantService.TenantId ?? Guid.Empty 
        };

        // 2. Save to Database
        _context.Clients.Add(newClient);
        await _context.SaveChangesAsync();

        // 3. Return the result
        return Ok(new { Message = "Client Created", ClientId = newClient.Id });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // This query is AUTOMATICALLY filtered by TenantId due to your Global Filter!
        var clients = await _context.Clients.ToListAsync();
        return Ok(clients);
    }
}