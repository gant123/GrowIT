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
    [MaxLength(200, ErrorMessage = "Keep the title under 200 characters.")]
    public string Title { get; set; } = string.Empty; // The Headline: "Got a Job", "Passed Math"

    [Required]
    public ImprintCategory Category { get; set; }

    [Required]
    public ImpactOutcome Outcome { get; set; } // Improved, Stable, Regressed

    public DateTime DateOccurred { get; set; } = DateTime.UtcNow; // When did it happen?

    [MaxLength(4000, ErrorMessage = "Keep \"What changed?\" under 4,000 characters.")]
    public string Notes { get; set; } = string.Empty;

    /// <summary>When set, a follow-up for this person is added to Follow-ups for that date.</summary>
    public DateTime? FollowupDate { get; set; }
}

/// <summary>
/// Edit a saved outcome. The person it belongs to stays the same (delete and re-add it to move it
/// to someone else). Changing or clearing the follow-up date updates the linked follow-up.
/// </summary>
public class UpdateImprintRequest
{
    public Guid? FamilyMemberId { get; set; }

    public Guid? InvestmentId { get; set; }

    [Required(ErrorMessage = "Give the outcome a short title.")]
    [MaxLength(200, ErrorMessage = "Keep the title under 200 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required]
    public ImprintCategory Category { get; set; }

    [Required]
    public ImpactOutcome Outcome { get; set; }

    public DateTime DateOccurred { get; set; }

    [MaxLength(4000, ErrorMessage = "Keep \"What changed?\" under 4,000 characters.")]
    public string Notes { get; set; } = string.Empty;

    public DateTime? FollowupDate { get; set; }
}
