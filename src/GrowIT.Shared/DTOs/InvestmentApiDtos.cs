using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

using System.ComponentModel.DataAnnotations;

public class InvestmentQueryParams
{
    public Guid? PersonId { get; set; }
    public InvestmentStatus? Status { get; set; }
    [MaxLength(200)]
    public string? SearchTerm { get; set; }
    [Range(1, 100000)]
    public int PageNumber { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

/// <summary>
/// Edit a service record. The person stays the same (delete and re-record to move it to someone
/// else). Cost, money source and program can only change while the record is draft or pending;
/// see <see cref="ServiceRecordRules.MoneyLocked"/>.
/// </summary>
public class UpdateInvestmentRequest : IValidatableObject
{
    public Guid? FamilyMemberId { get; set; }

    [GrowIT.Shared.Validation.RequiredGuid(ErrorMessage = "Choose the money source that paid for it.")]
    public Guid FundId { get; set; }

    [GrowIT.Shared.Validation.RequiredGuid(ErrorMessage = "Choose the program or service.")]
    public Guid ProgramId { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999.99", ErrorMessage = "Amount must be greater than 0.")]
    public decimal Amount { get; set; }

    [MaxLength(200, ErrorMessage = "Keep who was paid under 200 characters.")]
    public string PayeeName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Describe what was provided.")]
    [MinLength(3, ErrorMessage = "Describe what was provided in at least 3 characters.")]
    [MaxLength(2000, ErrorMessage = "Keep the description under 2,000 characters.")]
    public string Reason { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FamilyMemberId == Guid.Empty)
            yield return new ValidationResult("Household member must be blank or a valid person.", new[] { nameof(FamilyMemberId) });
    }
}

public class ReassignInvestmentRequest
{
    public Guid? NewFamilyMemberId { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(1000)]
    public string ReassignReason { get; set; } = string.Empty;
}
