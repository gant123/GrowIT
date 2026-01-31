using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateImprintRequest
{
    [Required]
    public Guid ClientId { get; set; } // Context: Which family?

    public Guid? FamilyMemberId { get; set; } // Specific Person: Who achieved this?

    public Guid? InvestmentId { get; set; } // Optional: Was this caused by a specific funding source?

    [Required]
    public string Title { get; set; } = string.Empty; // The Headline: "Got a Job", "Passed Math"

    [Required]
    public ImprintCategory Category { get; set; }

    [Required]
    public ImpactOutcome Outcome { get; set; } // Improved, Stable, Regressed

    public DateTime DateOccurred { get; set; } = DateTime.UtcNow; // When did it happen?

    public string Notes { get; set; } = string.Empty; 

    public DateTime? FollowupDate { get; set; }
}