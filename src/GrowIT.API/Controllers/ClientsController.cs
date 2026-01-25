
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
        // 1. Fetch Client with related data (Investments)
        // Note: Ensure your DbContext has .Include(c => c.Investments) if relationships are set up.
        // For now, we will query them separately to be safe.
        
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == id); // Tenant filter handled by Global Query Filter usually, or add manually

        if (client == null) return NotFound("Client not found.");

        // 2. Fetch Investments (The Financial History)
        var investments = await _context.Investments
            .Where(i => i.ClientId == id)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        // 3. Build the Timeline
        var timeline = new List<TimelineItemDto>();

        // Add Investments to Timeline
        foreach (var inv in investments)
        {
            timeline.Add(new TimelineItemDto
            {
                Id = inv.Id,
                Date = inv.CreatedAt,
                Type = "Investment",
                Title = $"Investment: {inv.PayeeName}", // e.g., "Entergy"
                Description = inv.Reason,
                Amount = inv.Amount,
                Icon = "oi-dollar",
                ColorClass = "text-success"
            });
        }

        // (Future: Add Imprints/Notes to this same list here)

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
            
            // Stats
            TotalInvestment = investments.Sum(x => x.Amount),
            LastInvestmentDate = investments.FirstOrDefault()?.CreatedAt,
            
            // Sort timeline by newest first
            Timeline = timeline.OrderByDescending(t => t.Date).ToList()
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
}