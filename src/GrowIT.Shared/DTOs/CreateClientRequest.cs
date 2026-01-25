using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateClientRequest
{
   
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    
    // Intake details
    public int HouseholdCount { get; set; }
    public int StabilityScore { get; set; } // 1-10
    
    // STRICT ENUM: This fixes the "operator ??" error
    public LifePhase LifePhase { get; set; } = LifePhase.Crisis;
    
    // Optional linking
    public Guid? HouseholdId { get; set; }
}