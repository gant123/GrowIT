using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Core.Interfaces;

namespace GrowIT.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DashboardController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var now = DateTime.UtcNow;
        
        // *** FIX: Explicitly tell Postgres this is a UTC Date ***
        var startOfYear = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // 1. Calculate KPIs
        var investmentsYtd = await _context.Investments
            .Where(i => i.CreatedAt >= startOfYear)
            .ToListAsync();

        var totalInvested = investmentsYtd.Sum(i => i.Amount);
        
        var uniqueFamilies = investmentsYtd
            .Select(i => i.ClientId)
            .Distinct()
            .Count();

        var fundsTotal = await _context.Funds.SumAsync(f => f.AvailableAmount);

        // 2. Recent Activity (Last 5 Investments)
        var recent = await _context.Investments
            .Include(i => i.Client)
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new ActivityItem
            {
                Id = i.Id,
                // Handle cases where Client might be null (though unlikely in prod)
                Description = i.Client != null 
                    ? $"Invested {i.Amount:C0} in {i.Client.FirstName} {i.Client.LastName}"
                    : $"Invested {i.Amount:C0} (Unknown Client)",
                Date = i.CreatedAt,
                Icon = "oi-dollar",
                Color = "text-success"
            })
            .ToListAsync();

        // 3. Assemble
        var stats = new DashboardStatsDto
        {
            TotalInvestedYtd = totalInvested,
            HouseholdsServedYtd = uniqueFamilies,
            ActiveCases = uniqueFamilies,
            FundsAvailable = fundsTotal,
            RecentActivity = recent,
            PendingFollowUps = new List<TaskItem>() // Empty list for now to prevent null error
        };

        return Ok(stats);
    }
}