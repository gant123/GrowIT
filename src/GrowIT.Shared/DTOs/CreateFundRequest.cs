using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateFundRequest
{
    [Required]
    public string Name { get; set; } = string.Empty; // e.g. "General Relief Fund"

    [Required]
    public decimal TotalAmount { get; set; } // The starting budget
}