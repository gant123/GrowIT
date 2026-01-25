using FluentValidation;
using GrowIT.Shared.DTOs;
using GrowIT.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Validators;

public class UpdateFundRequestValidator : AbstractValidator<UpdateFundRequest>
{
    // We inject the DB Context to do "Complex" checks (like checking current usage)
    public UpdateFundRequestValidator(ApplicationDbContext context, Guid fundId)
    {
        // 1. Simple Rules
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Fund Name is required.")
            .MaximumLength(100);

        RuleFor(x => x.ChangeReason)
            .NotEmpty().WithMessage("You must explain the reason for this change.")
            .MinimumLength(5).WithMessage("Please provide a descriptive reason.");

        // 2. Complex Rule: "Budget cannot be lower than actual usage"
        RuleFor(x => x.TotalAmount)
            .GreaterThan(0).WithMessage("Budget must be positive.")
            .MustAsync(async (amount, cancellation) => 
            {
                // Calculate real usage from DB
                var realUsage = await context.Investments
                    .Where(i => i.FundId == fundId)
                    .SumAsync(i => i.Amount, cancellation);

                // Validation passes if New Amount >= Real Usage
                return amount >= realUsage;
            })
            .WithMessage(x => $"Cannot reduce budget to {x.TotalAmount:C0}. You have already spent funds exceeding this amount.");
    }
}