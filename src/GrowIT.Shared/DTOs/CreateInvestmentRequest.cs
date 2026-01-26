namespace GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using System.ComponentModel.DataAnnotations;
public class CreateInvestmentRequest
{
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; } 
    public Guid FundId { get; set; }
    public Guid ProgramId { get; set; }
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class Investment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Core Investment Data
    public Guid PersonId { get; set; }
    public Person? Person { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
    
    // Categorization
    public InvestmentCategory Category { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Funding Source
    public string? FundingSource { get; set; }
    public string? GrantId { get; set; }
    public string? DonorName { get; set; }
    
    // Vendor/Recipient
    public string? VendorName { get; set; }
    public string? VendorAddress { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Check;
    public string? CheckNumber { get; set; }
    public string? TransactionReference { get; set; }
    
    // Commitment (if applicable)
    public bool RequiresCommitment { get; set; }
    public string? CommitmentDescription { get; set; }
    public CommitmentStatus CommitmentStatus { get; set; } = CommitmentStatus.NotRequired;
    public DateTime? CommitmentDueDate { get; set; }
    public DateTime? CommitmentCompletedDate { get; set; }
    
    // Linked to Growth Plan
    public Guid? GrowthPlanId { get; set; }
    public GrowthPlan? GrowthPlan { get; set; }
    
    // Fiscal Year
    public string FiscalYear { get; set; } = string.Empty;
    
    // System Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
   public InvestmentStatus Status { get; set; } = InvestmentStatus.Pending;
    public string? Notes { get; set; }
    
    // Attachments/Documentation
    public List<InvestmentDocument> Documents { get; set; } = new();
}

/// <summary>
/// Categories of investments aligned with common nonprofit assistance types.
/// </summary>
public enum InvestmentCategory
{
    // Basic Needs
    Food,
    Housing,
    Utilities,
    Transportation,
    Clothing,
    
    // Healthcare
    Medical,
    Dental,
    Vision,
    MentalHealth,
    Prescriptions,
    
    // Education & Employment
    Education,
    JobTraining,
    Childcare,
    
    // Financial
    DebtRelief,
    EmergencyFund,
    
    // Other
    Legal,
    Technology,
    Household,
    Other
}

public enum PaymentMethod
{
    Check,
    BankTransfer,
    Cash,
    GiftCard,
    DirectToVendor,
    InKind
}

public enum InvestmentStatus
{
    Draft,
    Pending,
    Approved,
    Disbursed,
    Completed,
    Cancelled,
    Returned
}

public enum CommitmentStatus
{
    NotRequired,
    Pending,
    InProgress,
    Completed,
    Partial,
    Waived
}

public class InvestmentDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvestmentId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public long FileSize { get; set; }
    public DocumentType Type { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string? UploadedBy { get; set; }
}

public enum DocumentType
{
    Receipt,
    Invoice,
    Application,
    Approval,
    Commitment,
    Photo,
    Other
}

// DTOs
public class InvestmentListDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string PersonName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public InvestmentStatus Status { get; set; }
}

public class InvestmentDetailDto : InvestmentListDto
{
    public string? Description { get; set; }
    public string? GrantId { get; set; }
    public string? DonorName { get; set; }
    public string? VendorName { get; set; }
    public string? VendorAddress { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? CheckNumber { get; set; }
    public string? TransactionReference { get; set; }
    public bool RequiresCommitment { get; set; }
    public string? CommitmentDescription { get; set; }
    public CommitmentStatus CommitmentStatus { get; set; }
    public DateTime? CommitmentDueDate { get; set; }
    public Guid? GrowthPlanId { get; set; }
    public string? GrowthPlanTitle { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Notes { get; set; }
    public List<InvestmentDocumentDto> Documents { get; set; } = new();
}

public class InvestmentDocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public DocumentType Type { get; set; }
    public DateTime UploadedAt { get; set; }
}

public class InvestmentCreateDto
{
    public Guid PersonId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public InvestmentCategory Category { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? FundingSource { get; set; }
    public string? GrantId { get; set; }
    public string? VendorName { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public bool RequiresCommitment { get; set; }
    public string? CommitmentDescription { get; set; }
    public DateTime? CommitmentDueDate { get; set; }
    public Guid? GrowthPlanId { get; set; }
    public string? Notes { get; set; }
}

public class InvestmentUpdateDto : InvestmentCreateDto
{
    public InvestmentStatus Status { get; set; }
    public CommitmentStatus CommitmentStatus { get; set; }
    public string? CheckNumber { get; set; }
    public string? TransactionReference { get; set; }
}

// Aggregation DTOs for reporting
public class InvestmentSummaryByCategory
{
    public InvestmentCategory Category { get; set; }
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
}

public class InvestmentSummaryByFiscalYear
{
    public string FiscalYear { get; set; } = string.Empty;
    public int TotalInvestments { get; set; }
    public decimal TotalAmount { get; set; }
    public int UniquePeople { get; set; }
    public decimal AveragePerPerson { get; set; }
}

public class InvestmentSummaryByFundingSource
{
    public string FundingSource { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal UtilizationPercentage { get; set; }
}

public class CreateClientModel
{
    [Required(ErrorMessage = "Organization Name is required")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number")]
    public string Phone { get; set; } = string.Empty;
}

public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Identity
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? PreferredName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    
    public string FullName => string.IsNullOrWhiteSpace(PreferredName) 
        ? $"{FirstName} {LastName}".Trim() 
        : PreferredName;
    
    public string Initials => $"{FirstName?.FirstOrDefault()}{LastName?.FirstOrDefault()}".ToUpperInvariant();
    
    // Contact
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public Address? Address { get; set; }
    public ContactPreference PreferredContactMethod { get; set; } = ContactPreference.Phone;
    
    // Demographics
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public string? ReferralSource { get; set; }
    
    // Stability Tracking (0-100 scale)
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; } = Season.Planting;
    
    // Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    
    // System Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    
    // Related Data
    public List<GrowthPlan> GrowthPlans { get; set; } = new();
    public List<Investment> Investments { get; set; } = new();
    public List<Imprint> Imprints { get; set; } = new();
    public List<PersonTag> Tags { get; set; } = new();
}

public class Address
{
    public string? Street1 { get; set; }
    public string? Street2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? County { get; set; }
    
    public string FormattedAddress => string.Join(", ", 
        new[] { Street1, Street2, City, State, ZipCode }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
    
    public bool IsComplete => !string.IsNullOrWhiteSpace(Street1) 
        && !string.IsNullOrWhiteSpace(City) 
        && !string.IsNullOrWhiteSpace(State) 
        && !string.IsNullOrWhiteSpace(ZipCode);
}

public class PersonTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

/// <summary>
/// Season represents the current phase of a person's journey.
/// Based on the agricultural metaphor in the grow.IT vision.
/// </summary>
public enum Season
{
    /// <summary>Crisis Season - Immediate stabilization needed (red)</summary>
    Crisis = 0,
    
    /// <summary>Planting Season - Building foundation and resources (yellow/amber)</summary>
    Planting = 1,
    
    /// <summary>Growing Season - Developing independence (blue)</summary>
    Growing = 2,
    
    /// <summary>Harvest Season - Thriving and potentially giving back (green)</summary>
    Harvest = 3
}

public enum ContactPreference
{
    Phone,
    Email,
    Text,
    Mail
}

public enum EmploymentStatus
{
    Employed,
    PartTime,
    Unemployed,
    SelfEmployed,
    Retired,
    Disabled,
    Student,
    Unknown
}

public enum HousingStatus
{
    Own,
    Rent,
    Transitional,
    Homeless,
    WithFamily,
    Shelter,
    Unknown
}

// DTOs for API communication
public class PersonListDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; }
    public int TotalInvestments { get; set; }
    public decimal TotalInvested { get; set; }
    public DateTime? LastContactDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class PersonDetailDto : PersonListDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PreferredName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public Address? Address { get; set; }
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public string? ReferralSource { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public List<GrowthPlanSummaryDto> GrowthPlans { get; set; } = new();
    public List<InvestmentSummaryDto> RecentInvestments { get; set; } = new();
    public List<ImprintSummaryDto> RecentImprints { get; set; } = new();
}

public class PersonCreateDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address? Address { get; set; }
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public string? ReferralSource { get; set; }
    public string? Notes { get; set; }
}

public class PersonUpdateDto : PersonCreateDto
{
    public DateTime? DateOfBirth { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
}

// Placeholder DTOs for related entities
public class GrowthPlanSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public int GoalsCompleted { get; set; }
    public int TotalGoals { get; set; }
}

public class InvestmentSummaryDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? FundingSource { get; set; }
    public DateTime Date { get; set; }
}

public class ImprintSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}


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
