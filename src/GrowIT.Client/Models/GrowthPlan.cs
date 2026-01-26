namespace GrowIT.Client.Models;

/// <summary>
/// Growth Plan - A structured journey plan for a person's development.
/// Replaces the traditional "Case File" terminology with growth-focused language.
/// </summary>
public class GrowthPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Core Data
    public Guid PersonId { get; set; }
    public Person? Person { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Season/Phase Tracking
    public Season Season { get; set; } = Season.Planting;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? TargetEndDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    
    // Goals and Progress
    public List<GrowthGoal> Goals { get; set; } = new();
    public int CompletedGoalsCount => Goals.Count(g => g.IsCompleted);
    public decimal ProgressPercentage => Goals.Count > 0 
        ? (decimal)CompletedGoalsCount / Goals.Count * 100 
        : 0;
    
    // Associated Data
    public List<Investment> Investments { get; set; } = new();
    public List<Imprint> Imprints { get; set; } = new();
    public List<GrowthPlanNote> Notes { get; set; } = new();
    
    // Status
    public GrowthPlanStatus Status { get; set; } = GrowthPlanStatus.Active;
    public string? ClosureReason { get; set; }
    
    // Assignment
    public string? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }
    
    // System Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

public class GrowthGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GrowthPlanId { get; set; }
    
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GoalCategory Category { get; set; }
    public GoalPriority Priority { get; set; } = GoalPriority.Medium;
    
    public DateTime? TargetDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsCompleted => CompletedDate.HasValue;
    
    public List<GrowthMilestone> Milestones { get; set; } = new();
    
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GrowthMilestone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoalId { get; set; }
    
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedDate { get; set; }
    
    public int Order { get; set; }
}

public class GrowthPlanNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GrowthPlanId { get; set; }
    
    public string Content { get; set; } = string.Empty;
    public NoteType Type { get; set; } = NoteType.General;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}

public enum GrowthPlanStatus
{
    Draft,
    Active,
    OnHold,
    Completed,
    Graduated,
    Closed
}

public enum GoalCategory
{
    Housing,
    Employment,
    Education,
    Financial,
    Health,
    Family,
    Legal,
    Transportation,
    SocialSupport,
    LifeSkills,
    Other
}

public enum GoalPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum NoteType
{
    General,
    ContactLog,
    Progress,
    Concern,
    Achievement
}

/// <summary>
/// Imprint - Documented outcomes and impact measurements.
/// Captures the "mark" left by investments and support, 
/// providing evidence for grant reporting and impact demonstration.
/// </summary>
public class Imprint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Core Data
    public Guid PersonId { get; set; }
    public Person? Person { get; set; }
    public Guid? GrowthPlanId { get; set; }
    public GrowthPlan? GrowthPlan { get; set; }
    public Guid? InvestmentId { get; set; }
    public Investment? Investment { get; set; }
    
    // Outcome Details
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImprintCategory Category { get; set; }
    public ImprintType Type { get; set; }
    
    // Quantitative Data (when applicable)
    public decimal? QuantitativeValue { get; set; }
    public string? QuantitativeUnit { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? ChangeValue => QuantitativeValue.HasValue && BaselineValue.HasValue 
        ? QuantitativeValue.Value - BaselineValue.Value 
        : null;
    public decimal? PercentageChange => BaselineValue.HasValue && BaselineValue.Value != 0 && ChangeValue.HasValue
        ? (ChangeValue.Value / BaselineValue.Value) * 100
        : null;
    
    // Qualitative Data
    public string? QualitativeNotes { get; set; }
    public string? PersonQuote { get; set; }
    
    // Verification
    public bool IsVerified { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNotes { get; set; }
    
    // Documentation
    public List<ImprintDocument> Documents { get; set; } = new();
    
    // Timing
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string FiscalYear { get; set; } = string.Empty;
    
    // System Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

public class ImprintDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ImprintId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public enum ImprintCategory
{
    // Stability Outcomes
    HousingStability,
    FinancialStability,
    EmploymentStability,
    FoodSecurity,
    
    // Health Outcomes
    PhysicalHealth,
    MentalHealth,
    HealthcareAccess,
    
    // Education Outcomes
    EducationalAttainment,
    SkillDevelopment,
    Certification,
    
    // Family Outcomes
    FamilyWellbeing,
    ChildWelfare,
    ParentingSkills,
    
    // Community Integration
    SocialConnection,
    CommunityEngagement,
    CivicParticipation,
    
    // Self-Sufficiency
    Independence,
    GoalAchievement,
    LifeSkills,
    
    Other
}

public enum ImprintType
{
    /// <summary>Direct measurable outcome (job obtained, debt paid)</summary>
    DirectOutcome,
    
    /// <summary>Progress toward a goal (completed training, saved money)</summary>
    Progress,
    
    /// <summary>Maintained stability (remained housed, kept job)</summary>
    Maintenance,
    
    /// <summary>Improvement in conditions (income increased, credit improved)</summary>
    Improvement,
    
    /// <summary>Prevented negative outcome (eviction prevented, avoided crisis)</summary>
    Prevention,
    
    /// <summary>Story/testimony capturing qualitative impact</summary>
    Story
}

// DTOs
public class GrowthPlanListDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public GrowthPlanStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? TargetEndDate { get; set; }
    public int CompletedGoals { get; set; }
    public int TotalGoals { get; set; }
    public decimal ProgressPercentage { get; set; }
    public string? AssignedToUserName { get; set; }
}

public class GrowthPlanDetailDto : GrowthPlanListDto
{
    public string? Description { get; set; }
    public DateTime? ActualEndDate { get; set; }
    public string? ClosureReason { get; set; }
    public List<GrowthGoalDto> Goals { get; set; } = new();
    public List<InvestmentSummaryDto> Investments { get; set; } = new();
    public List<ImprintSummaryDto> Imprints { get; set; } = new();
    public List<GrowthPlanNoteDto> Notes { get; set; } = new();
}

public class GrowthGoalDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GoalCategory Category { get; set; }
    public GoalPriority Priority { get; set; }
    public DateTime? TargetDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public bool IsCompleted { get; set; }
    public List<GrowthMilestoneDto> Milestones { get; set; } = new();
    public int Order { get; set; }
}

public class GrowthMilestoneDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedDate { get; set; }
    public int Order { get; set; }
}

public class GrowthPlanNoteDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public NoteType Type { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

public class ImprintListDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ImprintCategory Category { get; set; }
    public ImprintType Type { get; set; }
    public DateTime Date { get; set; }
    public string FiscalYear { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public decimal? QuantitativeValue { get; set; }
    public string? QuantitativeUnit { get; set; }
}

public class ImprintDetailDto : ImprintListDto
{
    public Guid? GrowthPlanId { get; set; }
    public string? GrowthPlanTitle { get; set; }
    public Guid? InvestmentId { get; set; }
    public string? Description { get; set; }
    public decimal? BaselineValue { get; set; }
    public decimal? ChangeValue { get; set; }
    public decimal? PercentageChange { get; set; }
    public string? QualitativeNotes { get; set; }
    public string? PersonQuote { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNotes { get; set; }
    public List<ImprintDocumentDto> Documents { get; set; } = new();
}

public class ImprintDocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
}

public class ImprintCreateDto
{
    public Guid PersonId { get; set; }
    public Guid? GrowthPlanId { get; set; }
    public Guid? InvestmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ImprintCategory Category { get; set; }
    public ImprintType Type { get; set; }
    public decimal? QuantitativeValue { get; set; }
    public string? QuantitativeUnit { get; set; }
    public decimal? BaselineValue { get; set; }
    public string? QualitativeNotes { get; set; }
    public string? PersonQuote { get; set; }
    public DateTime Date { get; set; }
}

// Aggregation DTOs for reporting
public class ImprintSummaryByCategory
{
    public ImprintCategory Category { get; set; }
    public int Count { get; set; }
    public int VerifiedCount { get; set; }
    public int UniquePeople { get; set; }
}

public class ImpactDashboardDto
{
    public string FiscalYear { get; set; } = string.Empty;
    
    // People Metrics
    public int TotalPeopleServed { get; set; }
    public int NewPeopleThisPeriod { get; set; }
    public int PeopleInCrisis { get; set; }
    public int PeopleThriving { get; set; }
    
    // Investment Metrics
    public int TotalInvestments { get; set; }
    public decimal TotalInvested { get; set; }
    public decimal AverageInvestmentPerPerson { get; set; }
    
    // Outcome Metrics
    public int TotalImprints { get; set; }
    public int VerifiedImprints { get; set; }
    public List<ImprintSummaryByCategory> ImprintsByCategory { get; set; } = new();
    
    // Progress Metrics
    public int ActiveGrowthPlans { get; set; }
    public int CompletedGrowthPlans { get; set; }
    public int GoalsAchieved { get; set; }
    
    // Season Distribution
    public int PeopleInCrisisSeason { get; set; }
    public int PeopleInPlantingSeason { get; set; }
    public int PeopleInGrowingSeason { get; set; }
    public int PeopleInHarvestSeason { get; set; }
}
