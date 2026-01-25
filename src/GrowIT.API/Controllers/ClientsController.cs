
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
}