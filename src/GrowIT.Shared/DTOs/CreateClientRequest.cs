using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateClientRequest
{
   
    [Required]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    public string LastName { get; set; } = string.Empty;

    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Address { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? SSNLast4 { get; set; }
    public string? PhotoUrl { get; set; }
    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Single;
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Other;
    
    // Intake details
    public int HouseholdCount { get; set; }
    public int StabilityScore { get; set; } // 1-10
    
    // STRICT ENUM: This fixes the "operator ??" error
    public LifePhase LifePhase { get; set; } = LifePhase.Crisis;
    
    // Optional linking
    public Guid? HouseholdId { get; set; }
    public HouseholdRole HouseholdRole { get; set; } = HouseholdRole.Head;
    public DateTime? NextFollowupDate { get; set; }
}
