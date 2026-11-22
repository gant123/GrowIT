using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;
using GrowIT.Shared.Validation;

namespace GrowIT.Shared.DTOs;

public class CreateImprintRequest
{
    // [Required] never fails for a Guid (an unselected dropdown is Guid.Empty, not null).
    [RequiredGuid(ErrorMessage = "Choose who this outcome belongs to.")]
    public Guid ClientId { get; set; } // Context: Which family?

    public Guid? FamilyMemberId { get; set; } // Specific Person: Who achieved this?

    public Guid? InvestmentId { get; set; } // Optional: Was this caused by a specific funding source?

    [Required(ErrorMessage = "Give the outcome a short title.")]
    public string Title { get; set; } = string.Empty; // The Headline: "Got a Job", "Passed Math"

    [Required]
    public ImprintCategory Category { get; set; }

    [Required]
    public ImpactOutcome Outcome { get; set; } // Improved, Stable, Regressed

    public DateTime DateOccurred { get; set; } = DateTime.UtcNow; // When did it happen?

    public string Notes { get; set; } = string.Empty; 

    public DateTime? FollowupDate { get; set; }
}