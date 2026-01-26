namespace GrowIT.Shared.DTOs;

public class DashboardStatsDto
{
    public int TotalClients { get; set; }
    public decimal TotalInvested { get; set; }
    public int TotalMilestones { get; set; }
    
    // Optional: Add other stats if needed later
    public int ActivePlans { get; set; }
    public int PendingInvestments { get; set; }
    // Top Row KPIs
    public decimal TotalInvestedYtd { get; set; }
    public int HouseholdsServedYtd { get; set; }
    public int ActiveCases { get; set; }
    public decimal FundsAvailable { get; set; }

    // "The Harvest" (Charts)
    public List<MonthlyMetric> MonthlyTrends { get; set; } = new();

    // "Recent Activity" (Feed)
    public List<ActivityItem> RecentActivity { get; set; } = new();
    
    // "To-Grow List" (Tasks)
    public List<TaskItem> PendingFollowUps { get; set; } = new();
}

public class MonthlyMetric
{
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class ActivityItem
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty; // "Planted $150 seed for John Doe"
    public DateTime Date { get; set; }
    public string Icon { get; set; } = "oi-check";
    public string Color { get; set; } = "text-success";
}

public class TaskItem
{
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
}