using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

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
