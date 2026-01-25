using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class UpdateFundRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Range(0, double.MaxValue, ErrorMessage = "Budget must be positive.")]
    public decimal TotalAmount { get; set; }

    [Required(ErrorMessage = "You must explain why you are changing the budget.")]
    [MinLength(5, ErrorMessage = "Please provide a meaningful reason.")]
    public string ChangeReason { get; set; } = string.Empty;
}