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
public enum InvestmentStatus { Pending, Approved, Rejected, Paid, Cancelled }