using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using System.Globalization;

namespace GrowIT.Backend.Controllers;

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

        // 1. KPI: Total Invested YTD (money that has actually left the organization)
        var totalInvestedYtd = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear &&
                (i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed))
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;

        // 2. KPI: Households Served YTD (Unique Clients with disbursed investments)
        var householdsServed = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear &&
                (i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed))
            .Select(i => i.ClientId)
            .Distinct()
            .CountAsync();

        // 3. KPI: Active Cases (Total Clients)
        var activeCases = await _context.Clients.CountAsync();

        // 4. KPI: Funds Available
        var fundsAvailable = await _context.Funds.SumAsync(f => (decimal?)f.AvailableAmount) ?? 0m;

        // 5. Total Lifetime Stats (Filling missing DTO fields)
        var totalLifetimeInvested = await _context.Investments
            .Where(i => i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed)
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        var totalMilestones = await _context.Imprints.CountAsync();

        // 6. CHART: Monthly Trends (Group by Month)
        var rawInvestments = await _context.Investments
            .Where(i => i.CreatedAt.Year == currentYear &&
                (i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed))
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
        // "Planted" implies money left the organization, so match the KPI statuses
        // rather than listing pending/draft requests as activity.
        var recentInvestments = await _context.Investments
            .Include(i => i.Client)
            .Where(i => i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed)
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .ToListAsync();

        var recentImprints = await _context.Imprints
            .OrderByDescending(i => i.DateOccurred)
            .Take(10)
            .Select(i => new
            {
                i.Id,
                i.Title,
                i.DateOccurred,
                i.Outcome,
                ClientFirstName = i.Client != null ? i.Client.FirstName : null
            })
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
            Description = $"Harvest: {m.Title} ({m.ClientFirstName ?? "Unknown"})",
            Date = m.DateOccurred,
            Icon = "oi-flag",
            Color = m.Outcome == ImpactOutcome.Improved ? "text-primary" : "text-warning"
        }));

        // 8. Follow-ups: pending action items.
        var followUps = await _context.Tasks
            .Include(t => t.Client)
            .Include(t => t.AssignedUser)
            .Where(t => t.Status == GrowIT.Shared.Enums.TaskStatus.Pending)
            .OrderBy(t => t.DueDate)
            .Take(10)
            .Select(t => new TaskItem
            {
                TaskId = t.Id,
                ClientId = t.ClientId,
                ClientName = t.Client != null ? $"{t.Client.FirstName} {t.Client.LastName}" : "Unknown",
                AssignedToName = t.AssignedUser != null
                    ? t.AssignedUser.FirstName + " " + t.AssignedUser.LastName
                    : "Unassigned",
                Note = t.Notes,
                DueDate = t.DueDate
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

    [HttpGet("insights")]
    public async Task<ActionResult<InsightsDto>> GetInsights()
    {
        var now = DateTime.UtcNow;
        var last30Days = now.AddDays(-30);
        var prev30Days = now.AddDays(-60);

        // 1. Funding Readiness
        var fundsAvailable = await _context.Funds.SumAsync(f => (decimal?)f.AvailableAmount) ?? 0m;
        var recentSpending = await _context.Investments
            .Where(i => i.CreatedAt >= last30Days &&
                (i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed))
            .SumAsync(i => (decimal?)i.Amount) ?? 0m;
        
        var burnRate = recentSpending; // Monthly burn rate
        var runway = burnRate > 0 ? (int)(fundsAvailable / burnRate) : 99;
        
        var score = 70; // Base score
        var suggestions = new List<string>();

        if (runway < 3)
        {
            score -= 30;
            suggestions.Add("Runway is critically low (under 3 months). Focus on immediate fundraising.");
        }
        else if (runway < 6)
        {
            score -= 10;
            suggestions.Add("Runway is moderate. Plan for next funding cycle within 60 days.");
        }
        else
        {
            score += 10;
            suggestions.Add("Healthy runway detected. Focus on deepening program impact.");
        }

        var unapprovedInvestments = await _context.Investments.CountAsync(i => i.Status == InvestmentStatus.Pending);
        if (unapprovedInvestments > 5)
        {
            score -= 5;
            suggestions.Add($"{unapprovedInvestments} pending investments need approval to maintain data accuracy.");
        }

        // 2. Impact Velocity
        var currentMilestones = await _context.Imprints.CountAsync(i => i.DateOccurred >= last30Days);
        var prevMilestones = await _context.Imprints.CountAsync(i => i.DateOccurred >= prev30Days && i.DateOccurred < last30Days);
        
        double growth = 0;
        if (prevMilestones > 0)
        {
            growth = ((double)currentMilestones - prevMilestones) / prevMilestones * 100;
        }

        // 3. Allocation Insights (by Program)
        var allocation = await _context.Investments
            .Where(i => i.Status == InvestmentStatus.Disbursed || i.Status == InvestmentStatus.Completed)
            .Include(i => i.Program)
            .GroupBy(i => i.Program!.Name ?? "Uncategorized")
            .Select(g => new CategoryDistributionDto
            {
                Category = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        var totalAllocated = allocation.Sum(a => a.Amount);
        foreach (var a in allocation)
        {
            a.Percentage = totalAllocated > 0 ? (double)(a.Amount / totalAllocated * 100) : 0;
        }

        // 4. Program Performance (Top Milestones by Program/Category)
        // Group in SQL, but convert the enum key to its name in memory — enum ToString()
        // is not reliably translatable by EF/Npgsql.
        var topPrograms = (await _context.Imprints
                .GroupBy(i => i.Category)
                .Select(g => new { g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(5)
                .ToListAsync())
            .Select(g => new TopPerformerDto
            {
                Name = g.Key.ToString(),
                Count = g.Count
            })
            .ToList();

        return Ok(new InsightsDto
        {
            FundingReadiness = new FundingReadinessDto
            {
                Score = Math.Clamp(score, 0, 100),
                Suggestions = suggestions,
                BurnRateMonthly = burnRate,
                EstimatedRunwayMonths = runway
            },
            ImpactVelocity = new ImpactVelocityDto
            {
                CurrentVelocity = currentMilestones,
                PreviousVelocity = prevMilestones,
                GrowthPercentage = growth
            },
            AllocationInsights = allocation.OrderByDescending(a => a.Amount).ToList(),
            ProgramPerformance = topPrograms
        });
    }
}
