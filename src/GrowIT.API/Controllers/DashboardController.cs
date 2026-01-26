using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using System.Globalization;

namespace GrowIT.API.Controllers;

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

        // 1. KPI: Total Invested YTD
        var totalInvestedYtd = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear)
            .SumAsync(i => i.Amount);

        // 2. KPI: Households Served YTD (Unique Clients)
        var householdsServed = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear)
            .Select(i => i.ClientId)
            .Distinct()
            .CountAsync();

        // 3. KPI: Active Cases (Total Clients)
        var activeCases = await _context.Clients.CountAsync();

        // 4. KPI: Funds Available
        var fundsAvailable = await _context.Funds.SumAsync(f => f.AvailableAmount);

        // 5. CHART: Monthly Trends (Group by Month)
        // Note: For Postgres, we pull data first then group in memory to keep it simple 
        // (or use specialized SQL DateTrunc if dataset is huge)
        var rawInvestments = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear)
            .Select(i => new { i.CreatedAt, i.Amount })
            .ToListAsync();

        var monthlyTrends = rawInvestments
            .GroupBy(i => i.CreatedAt.Month)
            .Select(g => new MonthlyMetric
            {
                Month = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(g.Key),
                Amount = g.Sum(x => x.Amount)
            })
            .ToList();

        // 6. FEED: Recent Activity (Merge Money & Milestones)
        var recentInvestments = await _context.Investments
            .Include(i => i.Client)
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .ToListAsync();

        var recentImprints = await _context.Imprints
            .Include(i => i.Client)
            .OrderByDescending(i => i.DateOccurred)
            .Take(5)
            .ToListAsync();

        var activityFeed = new List<ActivityItem>();

        // Map Investments ($)
        activityFeed.AddRange(recentInvestments.Select(i => new ActivityItem
        {
            Id = i.Id,
            Description = $"Planted ${i.Amount:N0} seed for {i.Client?.FirstName}",
            Date = i.CreatedAt,
            Icon = "oi-dollar",
            Color = "text-success"
        }));

        // Map Milestones (Flags)
        activityFeed.AddRange(recentImprints.Select(m => new ActivityItem
        {
            Id = m.Id,
            Description = $"Harvest: {m.Title} ({m.Client?.FirstName})",
            Date = m.DateOccurred,
            Icon = "oi-flag",
            Color = m.Outcome == ImpactOutcome.Improved ? "text-primary" : "text-warning"
        }));

        // 7. TASKS: Pending Follow-Ups
        // Find imprints that have a FollowupDate set for the future (or recently past)
        var followUps = await _context.Imprints
            .Include(i => i.Client)
            .Where(i => i.FollowupDate != null && i.FollowupDate >= DateTime.UtcNow.AddDays(-7)) // Show items due soon or missed last week
            .OrderBy(i => i.FollowupDate)
            .Take(5)
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