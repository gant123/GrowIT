using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class InvestmentUpdateDto : InvestmentCreateDto
{
    public InvestmentStatus Status { get; set; }
    public CommitmentStatus CommitmentStatus { get; set; }
    public string? CheckNumber { get; set; }
    public string? TransactionReference { get; set; }
}
