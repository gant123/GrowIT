using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

using System.ComponentModel.DataAnnotations;

public class InvestmentQueryParams
{
    public Guid? PersonId { get; set; }
    public InvestmentStatus? Status { get; set; }
    [MaxLength(200)]
    public string? SearchTerm { get; set; }
    [Range(1, 100000)]
    public int PageNumber { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public class ReassignInvestmentRequest
{
    public Guid? NewFamilyMemberId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(1000)]
    public string ReassignReason { get; set; } = string.Empty;
}

public class ApproveInvestmentRequest
{
    [MaxLength(200)]
    public string ApprovedBy { get; set; } = string.Empty;
}
