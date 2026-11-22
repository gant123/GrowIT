namespace GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using GrowIT.Shared.Validation;
using System.ComponentModel.DataAnnotations;
public class CreateInvestmentRequest : IValidatableObject
{
    // Messages use the words the form shows ("person", "money source"), and the selections are
    // attribute-validated so they are reported together with amount/description, not afterwards.
    [RequiredGuid(ErrorMessage = "Choose who received support.")]
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; } 
    [RequiredGuid(ErrorMessage = "Choose the money source that paid for it.")]
    public Guid FundId { get; set; }
    [RequiredGuid(ErrorMessage = "Choose the program or service.")]
    public Guid ProgramId { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999.99", ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string PayeeName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Describe what happened.")]
    [MinLength(3, ErrorMessage = "Describe what happened in at least 3 characters.")]
    [MaxLength(2000, ErrorMessage = "Keep the description under 2,000 characters.")]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FamilyMemberId == Guid.Empty)
            yield return new ValidationResult("Family member must be blank or a valid person.", new[] { nameof(FamilyMemberId) });
    }
}
