namespace GrowIT.Client.Models;
using GrowIT.Shared.Enums;
/// <summary>
/// Represents an Investment - resources given to support a person's growth.
/// In grow.IT terminology, these are "investments" not "handouts" - 
/// each dollar is tracked as a deliberate investment in someone's future.
/// </summary>
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
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public InvestmentCategory Category { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? FundingSource { get; set; }
    public InvestmentStatus Status { get; set; }
    public string FiscalYear { get; set; } = string.Empty;
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
