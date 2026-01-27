namespace GrowIT.Shared.Enums;

public enum SubscriptionPlanType { Free, Pro, Enterprise }
public enum SubscriptionStatus { Active, Trialing, PastDue, Canceled }
public enum InvoiceStatus { Draft, Open, Paid, Void, Uncollectible }
public enum PaymentMethod { Card, BankTransfer, Cash, Other }
public enum PaymentStatus { Pending, Succeeded, Failed, Refunded }

public enum HouseholdRole { Head, Spouse, Dependent, Other }
public enum MaritalStatus { Single, Married, Other }
public enum EmploymentStatus { Employed, Unemployed, Other }
public enum LifePhase { Crisis, Stable, Thriving }
public enum DocumentCategory { ID, Bill, Contract, Other }

public enum TaskStatus { Pending, Completed, Skipped }
public enum ImpactOutcome { Improved, Maintained, Regressed, Unknown }

public enum UserRole { Admin, CaseWorker, Viewer }
public enum UserStatus { Active, Inactive, Pending }
public enum NotificationType { Email, SMS, InApp }
public enum NotificationFrequency { Immediate, Daily, Weekly, Monthly }
public enum AuditAction { Create, Update, Delete, Access }
public enum ReportType { Usage, Financial, ClientProgress, SystemHealth }
public enum ExportFormat { CSV, PDF, Excel }
public enum IntegrationType { Stripe, Twilio, SendGrid, Other }
public enum EnvironmentType { Development, Staging, Production }
public enum LogLevel { Trace, Debug, Info, Warn, Error, Fatal }
public enum FeatureFlag { NewDashboard, AdvancedReporting, BetaFeatureX }
public enum DataRetentionPolicy { ThirtyDays, NinetyDays, OneYear, Indefinite }
public enum ComplianceStandard { GDPR, HIPAA, CCPA, None }
public enum BackupFrequency { Daily, Weekly, Monthly }
public enum BackupStatus { Pending, Completed, Failed }
public enum DeploymentStatus { InProgress, Successful, Failed }
public enum NotificationChannel { Email, SMS, PushNotification }
public enum UserActivityType { Login, Logout, DataChange, PermissionChange }
public enum SessionStatus { Active, Expired, Revoked }
public enum PasswordResetStatus { Requested, Completed, Expired }
public enum TwoFactorMethod { SMS, AuthenticatorApp, Email }
public enum TwoFactorStatus { Enabled, Disabled, Pending }
public enum ApiAccessLevel { ReadOnly, ReadWrite, Admin }
public enum RateLimitStatus { WithinLimit, Exceeded, Blocked }
public enum SystemHealthStatus { Healthy, Degraded, Unhealthy }
public enum MaintenanceWindow { Scheduled, InProgress, Completed, Canceled }
public enum NotificationPriority { Low, Medium, High }
public enum UserOnboardingStatus { NotStarted, InProgress, Completed }
public enum DataSyncStatus { Pending, InProgress, Completed, Failed }
public enum FeatureAccessLevel { Free, Pro, Enterprise }
public enum AuditSeverity { Low, Medium, High, Critical }
public enum IncidentStatus { Open, InProgress, Resolved, Closed }
public enum ChangeRequestStatus { Submitted, Approved, Rejected, Implemented }

// Investment-related Enums
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

public enum CommitmentStatus
{
    NotRequired,
    Pending,
    InProgress,
    Completed,
    Partial,
    Waived
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

// Imprint-related Enums
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

// Growth Plan-related Enums
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