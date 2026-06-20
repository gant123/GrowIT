namespace GrowIT.Shared.DTOs;

public class InsightsDto
{
    public FundingReadinessDto FundingReadiness { get; set; } = new();
    public ImpactVelocityDto ImpactVelocity { get; set; } = new();
    public List<CategoryDistributionDto> AllocationInsights { get; set; } = new();
    public List<TopPerformerDto> ProgramPerformance { get; set; } = new();
}

public class FundingReadinessDto
{
    public int Score { get; set; }
    public List<string> Suggestions { get; set; } = new();
    public decimal BurnRateMonthly { get; set; }
    public int EstimatedRunwayMonths { get; set; }
}

public class ImpactVelocityDto
{
    public int CurrentVelocity { get; set; }
    public int PreviousVelocity { get; set; }
    public double GrowthPercentage { get; set; }
}

public class CategoryDistributionDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}

public class TopPerformerDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
