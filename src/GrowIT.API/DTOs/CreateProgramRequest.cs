using System.ComponentModel.DataAnnotations;

namespace GrowIT.API.DTOs;

public class CreateProgramRequest
{
    [Required]
    public string Name { get; set; } = string.Empty; // e.g. "Utility Assistance"
    
    public string Description { get; set; } = string.Empty;
    
    public decimal DefaultUnitCost { get; set; } // e.g. $150.00
}