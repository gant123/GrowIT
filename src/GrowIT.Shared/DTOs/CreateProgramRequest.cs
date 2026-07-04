using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateProgramRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty; // e.g. "Utility Assistance"

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "999999999999.99", ErrorMessage = "Default cost cannot be negative.")]
    public decimal DefaultUnitCost { get; set; } // e.g. $150.00

    [Range(1, 100000)]
    public int? CapacityLimit { get; set; }

    [RegularExpression("^(Weekly|Monthly|Quarterly|Annual|Yearly)$", ErrorMessage = "Capacity period must be Weekly, Monthly, Quarterly, Annual, or Yearly.")]
    public string? CapacityPeriod { get; set; } // "Monthly", "Weekly", "Annual"
}
