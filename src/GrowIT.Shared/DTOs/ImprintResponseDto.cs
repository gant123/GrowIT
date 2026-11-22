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

    /// <summary>The household member if one was chosen, otherwise the person (head of household).</summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>Name of the person (head of household) the outcome belongs to.</summary>
    public string ClientName { get; set; } = string.Empty;

    /// <summary>Short description of the related service record, when one is linked.</summary>
    public string? InvestmentSummary { get; set; }
}
