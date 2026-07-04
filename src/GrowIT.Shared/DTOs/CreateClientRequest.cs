using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateClientRequest : IValidatableObject
{
   
    [Required]
    [MaxLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string LastName { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(254)]
    public string? Email { get; set; }

    [Phone]
    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string Address { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }

    [RegularExpression(@"^\d{4}$", ErrorMessage = "SSN last four must be exactly 4 digits.")]
    public string? SSNLast4 { get; set; }

    [Url]
    [MaxLength(2000)]
    public string? PhotoUrl { get; set; }
    public MaritalStatus MaritalStatus { get; set; } = MaritalStatus.Single;
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Other;
    
    // Intake details
    [Range(0, 100)]
    public int HouseholdCount { get; set; } = 1;

    [Range(1, 10)]
    public int StabilityScore { get; set; } = 5; // 1-10
    
    // STRICT ENUM: This fixes the "operator ??" error
    public LifePhase LifePhase { get; set; } = LifePhase.Crisis;
    
    // Optional linking
    public Guid? HouseholdId { get; set; }
    public HouseholdRole HouseholdRole { get; set; } = HouseholdRole.Head;
    public DateTime? NextFollowupDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HouseholdId == Guid.Empty)
            yield return new ValidationResult("Household must be blank or a valid household.", new[] { nameof(HouseholdId) });

        if (DateOfBirth.HasValue && DateOfBirth.Value.Date > DateTime.UtcNow.Date)
            yield return new ValidationResult("Date of birth cannot be in the future.", new[] { nameof(DateOfBirth) });

        if (NextFollowupDate.HasValue && NextFollowupDate.Value.Date < DateTime.UtcNow.Date.AddYears(-1))
            yield return new ValidationResult("Next follow-up date is too far in the past.", new[] { nameof(NextFollowupDate) });
    }
}
