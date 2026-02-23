using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class ImprintResponseDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; }
    public Guid? InvestmentId { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTime DateOccurred { get; set; }
    public ImprintCategory Category { get; set; }
    public ImpactOutcome Outcome { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime? FollowupDate { get; set; }
}
