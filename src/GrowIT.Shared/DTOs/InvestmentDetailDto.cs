using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

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
