namespace GrowIT.Shared.DTOs;

public class AscScoreDto
{
    public string Acronym { get; set; } = "ASC";
    public string FullName { get; set; } = "Accountability, Stability & Capacity";
    public string Label { get; set; } = "Organization Readiness Score";
    public bool IsScored { get; set; }
    public decimal? Score { get; set; }
    public string ScoreStatus { get; set; } = "Unscored";
    public string TierName { get; set; } = "Unscored";
    public string TierRange { get; set; } = "Not enough information";
    public string TierDescription { get; set; } = "Add the basic organization, program, financial, and impact information Grow.IT needs before it calculates a score.";
    public string Disclosure { get; set; } = "The ASC score is not a credit score, loan approval, or accounting certification. It is a Grow.IT readiness gauge based on saved organization data.";
    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;
    public AscScoreEvidenceDto Evidence { get; set; } = new();
    public List<AscScorePillarDto> Pillars { get; set; } = new();
    public List<string> MissingItems { get; set; } = new();
    public List<string> NextSteps { get; set; } = new();
    public List<AscScoreTierDto> Tiers { get; set; } = new();
}

public class AscScorePillarDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public string Status { get; set; } = "Needs Work";
    public List<string> Evidence { get; set; } = new();
    public List<string> MissingItems { get; set; } = new();
}

public class AscScoreEvidenceDto
{
    public bool HasOrganizationProfile { get; set; }
    public int ActiveUsers { get; set; }
    public int Clients { get; set; }
    public int HouseholdMembers { get; set; }
    public int Programs { get; set; }
    public int ProgramsWithUnitCosts { get; set; }
    public int ProgramsWithCapacity { get; set; }
    public int Funds { get; set; }
    public decimal TotalFunds { get; set; }
    public decimal FundsAvailable { get; set; }
    public int Investments { get; set; }
    public int FundedInvestments { get; set; }
    public int InvestmentsLinkedToPeople { get; set; }
    public int Outcomes { get; set; }
    public int PositiveOrMaintainedOutcomes { get; set; }
    public int Documents { get; set; }
    public int DocumentCategories { get; set; }
    public int GrowthPlans { get; set; }
    public int Tasks { get; set; }
    public int CompletedTasks { get; set; }
    public int ReportsGenerated { get; set; }
    public int ReportsDownloaded { get; set; }
    public int ReportSchedules { get; set; }
    public int VerifiedActivityMonths { get; set; }
    public DateTime? FirstVerifiedActivityAtUtc { get; set; }
    public DateTime? LastVerifiedActivityAtUtc { get; set; }
}

public class AscScoreTierDto
{
    public string Name { get; set; } = string.Empty;
    public string Range { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}
