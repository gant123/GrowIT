using System.ComponentModel.DataAnnotations;

namespace GrowIT.API.DTOs;

public class CreateInvestmentRequest
{
    [Required]
    public Guid ClientId { get; set; } // Who gets the help?

    [Required]
    public Guid FundId { get; set; }   // Where does money come from?

    [Required]
    public Guid ProgramId { get; set; } // What is it for?

    [Required]
    [Range(0.01, 100000)] // Safety check: Value must be positive
    public decimal Amount { get; set; }

    public string PayeeName { get; set; } = string.Empty; // e.g. "Entergy"
    public string Reason { get; set; } = string.Empty; // e.g. "Shut-off notice"
}