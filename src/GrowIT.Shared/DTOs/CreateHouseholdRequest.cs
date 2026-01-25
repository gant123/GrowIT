using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateHouseholdRequest
{
    [Required]
    public string Name { get; set; } = string.Empty; // e.g. "The Gant Family"
    
    // Optional: We can set the head of household immediately if we know their ID
    public Guid? PrimaryClientId { get; set; }
}