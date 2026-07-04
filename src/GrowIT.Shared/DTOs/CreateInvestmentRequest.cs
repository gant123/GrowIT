namespace GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using System.ComponentModel.DataAnnotations;
public class CreateInvestmentRequest : IValidatableObject
{
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; } 
    public Guid FundId { get; set; }
    public Guid ProgramId { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999.99", ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    [MaxLength(200)]
    public string PayeeName { get; set; } = string.Empty;

    [Required]
    [MinLength(3)]
    [MaxLength(2000)]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ClientId == Guid.Empty)
            yield return new ValidationResult("Please select a client.", new[] { nameof(ClientId) });

        if (FundId == Guid.Empty)
            yield return new ValidationResult("Please select a fund.", new[] { nameof(FundId) });

        if (ProgramId == Guid.Empty)
            yield return new ValidationResult("Please select a program.", new[] { nameof(ProgramId) });

        if (FamilyMemberId == Guid.Empty)
            yield return new ValidationResult("Family member must be blank or a valid person.", new[] { nameof(FamilyMemberId) });
    }
}
