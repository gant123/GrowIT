using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using System.Globalization;

namespace GrowIT.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardStatsDto>> GetStats()
    {
        var currentYear = DateTime.UtcNow.Year;

        // 1. KPI: Total Invested YTD (Only Approved ones)
        var totalInvestedYtd = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear && i.Status == InvestmentStatus.Approved)
            .SumAsync(i => i.Amount);

        // 2. KPI: Households Served YTD (Unique Clients with Approved Investments)
        var householdsServed = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear && i.Status == InvestmentStatus.Approved)
            .Select(i => i.ClientId)
            .Distinct()
            .CountAsync();

        // 3. KPI: Active Cases (Total Clients)
        var activeCases = await _context.Clients.CountAsync();

        // 4. KPI: Funds Available
        var fundsAvailable = await _context.Funds.SumAsync(f => f.AvailableAmount);

        // 5. Total Lifetime Stats (Filling missing DTO fields)
        var totalLifetimeInvested = await _context.Investments
            .Where(i => i.Status == InvestmentStatus.Approved)
            .SumAsync(i => i.Amount);
        var totalMilestones = await _context.Imprints.CountAsync();

        // 6. CHART: Monthly Trends (Group by Month)
        var rawInvestments = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear && i.Status == InvestmentStatus.Approved)
            .Select(i => new { i.CreatedAt, i.Amount })
            .ToListAsync();

        var monthlyTrends = rawInvestments
            .GroupBy(i => i.CreatedAt.Month)
            .Select(g => new
            {
                MonthIndex = g.Key,
                Metric = new MonthlyMetric
                {
                    Month = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(g.Key),
                    Amount = g.Sum(x => x.Amount)
                }
            })
            .OrderBy(m => m.MonthIndex)
            .Select(m => m.Metric)
            .ToList();

        // Ensure all months are represented if needed, or just return what we have.
        // For now, returning what we have is consistent with previous implementation.

        // 7. FEED: Recent Activity (Merge Money & Milestones)
        var recentInvestments = await _context.Investments
            .Include(i => i.Client)
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .ToListAsync();

        var recentImprints = await _context.Imprints
            .Include(i => i.Client)
            .OrderByDescending(i => i.DateOccurred)
            .Take(10)
            .ToListAsync();

        var activityFeed = new List<ActivityItem>();

        // Map Investments ($)
        activityFeed.AddRange(recentInvestments.Select(i => new ActivityItem
        {
            Id = i.Id,
            Description = $"Planted ${i.Amount:N0} seed for {i.Client?.FirstName ?? "Unknown"}",
            Date = i.CreatedAt,
            Icon = "oi-dollar",
            Color = "text-success"
        }));

        // Map Milestones (Flags)
        activityFeed.AddRange(recentImprints.Select(m => new ActivityItem
        {
            Id = m.Id,
            Description = $"Harvest: {m.Title} ({m.Client?.FirstName ?? "Unknown"})",
            Date = m.DateOccurred,
            Icon = "oi-flag",
            Color = m.Outcome == ImpactOutcome.Improved ? "text-primary" : "text-warning"
        }));

        // 8. TASKS: Pending Follow-Ups
        var followUps = await _context.Imprints
            .Include(i => i.Client)
            .Where(i => i.FollowupDate != null && i.FollowupDate >= DateTime.UtcNow.AddDays(-7))
            .OrderBy(i => i.FollowupDate)
            .Take(10)
            .Select(i => new TaskItem
            {
                ClientId = i.ClientId,
                ClientName = i.Client != null ? $"{i.Client.FirstName} {i.Client.LastName}" : "Unknown",
                Note = $"Follow up on: {i.Title}",
                DueDate = i.FollowupDate!.Value
            })
            .ToListAsync();

        return Ok(new DashboardStatsDto
        {
            TotalClients = activeCases,
            TotalInvested = totalLifetimeInvested,
            TotalMilestones = totalMilestones,
            TotalInvestedYtd = totalInvestedYtd,
            HouseholdsServedYtd = householdsServed,
            ActiveCases = activeCases,
            FundsAvailable = fundsAvailable,
            MonthlyTrends = monthlyTrends,
            RecentActivity = activityFeed.OrderByDescending(a => a.Date).Take(10).ToList(),
            PendingFollowUps = followUps
        });
    }
}