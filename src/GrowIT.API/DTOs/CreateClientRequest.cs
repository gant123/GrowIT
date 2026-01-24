using System.ComponentModel.DataAnnotations;
using GrowIT.Core.Enums;

namespace GrowIT.API.DTOs;

public class CreateClientRequest
{
    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    
    // Intake details
    public int HouseholdCount { get; set; }
    public int StabilityScore { get; set; } // 1-10
    public LifePhase LifePhase { get; set; } = LifePhase.Crisis;
    
    // Linking to a family? (Optional for now)
    public Guid? HouseholdId { get; set; }
}