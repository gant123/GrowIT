using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateFundRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty; // e.g. "General Relief Fund"

    [Range(typeof(decimal), "0.01", "999999999999.99", ErrorMessage = "Budget must be greater than 0.")]
    public decimal TotalAmount { get; set; } // The starting budget
}
