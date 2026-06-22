using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class BillingOverviewDto
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public SubscriptionDto? CurrentSubscription { get; set; }
    public List<InvoiceDto> Invoices { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
    public List<BillingEventDto> Events { get; set; } = new();
    public bool StripeConfigured { get; set; }
    public string? SetupMessage { get; set; }
}

public class SubscriptionPlanDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int MaxUsers { get; set; }
    public int MaxClients { get; set; }
    public string FeaturesJson { get; set; } = "{}";
    public List<string> Features { get; set; } = new();
    public bool CanCheckoutMonthly { get; set; }
    public bool CanCheckoutYearly { get; set; }
}

public class PlanUsageDto
{
    public string PlanName { get; set; } = "Free";
    public int ClientsUsed { get; set; }
    public int ClientsMax { get; set; }
    public int UsersUsed { get; set; }
    public int UsersMax { get; set; }

    // A non-positive max means "unlimited" (fail open) — never reports at-limit.
    public bool AtClientLimit => ClientsMax > 0 && ClientsUsed >= ClientsMax;
    public bool AtUserLimit => UsersMax > 0 && UsersUsed >= UsersMax;
}

public class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public SubscriptionStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public string? ExternalSubscriptionId { get; set; }
}

public class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string PaymentProvider { get; set; } = string.Empty;
    public string ExternalPaymentId { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BillingEventDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ReferenceTable { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class CreateCheckoutSessionRequest
{
    public Guid PlanId { get; set; }
    public BillingInterval Interval { get; set; } = BillingInterval.Monthly;
}

public class ActivatePlanRequest
{
    public Guid PlanId { get; set; }
}

// Result of a direct (non-Stripe) plan activation — used for free plans and for the
// demo path where Stripe is not configured. Lets the UI confirm and refresh limits.
public class PlanChangeResultDto
{
    public string PlanName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class BillingRedirectResponse
{
    public string Url { get; set; } = string.Empty;
}

public class CreateSubscriptionPlanRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int MaxUsers { get; set; }
    public int MaxClients { get; set; }
    public string FeaturesJson { get; set; } = "{}";
}

public class UpdateSubscriptionPlanRequest : CreateSubscriptionPlanRequest
{
}
