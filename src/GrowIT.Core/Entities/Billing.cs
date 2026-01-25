using GrowIT.Shared.Enums;
using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class SubscriptionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    public int MaxUsers { get; set; }
    public int MaxClients { get; set; }
    public string FeaturesJson { get; set; } = "{}"; // Store JSON as string
}

public class Subscription : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid PlanId { get; set; }
    public SubscriptionPlan? Plan { get; set; }

    public SubscriptionStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public string? ExternalSubscriptionId { get; set; } // Stripe Sub ID
}

public class Invoice : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid SubscriptionId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal AmountDue { get; set; }
    public decimal AmountPaid { get; set; }
    public InvoiceStatus Status { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class Payment : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string PaymentProvider { get; set; } = string.Empty; // "Stripe"
    public string ExternalPaymentId { get; set; } = string.Empty; // "ch_12345"
    public PaymentStatus Status { get; set; }
    
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}