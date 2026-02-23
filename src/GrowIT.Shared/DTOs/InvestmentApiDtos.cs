using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class InvestmentQueryParams
{
    public Guid? PersonId { get; set; }
    public InvestmentStatus? Status { get; set; }
    public string? SearchTerm { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ReassignInvestmentRequest
{
    public Guid? NewFamilyMemberId { get; set; }
    public string ReassignReason { get; set; } = string.Empty;
}

public class ApproveInvestmentRequest
{
    public string ApprovedBy { get; set; } = string.Empty;
}
