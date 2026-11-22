using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateHouseholdRequest
{
    [Required(ErrorMessage = "Enter a household name.")]
    [MaxLength(120, ErrorMessage = "Household name must be 120 characters or fewer.")]
    public string Name { get; set; } = string.Empty; // e.g. "The Gant Family"
    
    // Optional: We can set the head of household immediately if we know their ID
    public Guid? PrimaryClientId { get; set; }
}

/// <summary>Rename a household (PUT api/households/{id}).</summary>
public class UpdateHouseholdRequest
{
    [Required(ErrorMessage = "Enter a household name.")]
    [MaxLength(120, ErrorMessage = "Household name must be 120 characters or fewer.")]
    public string Name { get; set; } = string.Empty;
}