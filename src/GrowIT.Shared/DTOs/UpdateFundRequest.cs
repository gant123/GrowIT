using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class UpdateFundRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "999999999999.99", ErrorMessage = "Budget must be greater than 0.")]
    public decimal TotalAmount { get; set; }

    [Required(ErrorMessage = "You must explain why you are changing the budget.")]
    [MinLength(5, ErrorMessage = "Please provide a meaningful reason.")]
    [MaxLength(1000)]
    public string ChangeReason { get; set; } = string.Empty;
}
