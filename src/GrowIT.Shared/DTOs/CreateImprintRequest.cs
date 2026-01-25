using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateImprintRequest
{
    [Required]
    public Guid InvestmentId { get; set; } // Which investment are we grading?

    [Required]
    public ImpactOutcome Outcome { get; set; }// Improved, Maintained, Regressed, Unknown (This shit is broke will need to fix...i Just did a CAST on the section i need in teh ImprinsCOntroller)

    public string Notes { get; set; } = string.Empty; // "Client found a new job"

    public DateTime? FollowupDate { get; set; } // When should we check again?
}