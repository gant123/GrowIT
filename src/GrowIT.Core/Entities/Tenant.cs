using GrowIT.Shared.Enums;

namespace GrowIT.Core.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public SubscriptionPlanType SubscriptionPlan { get; set; } = SubscriptionPlanType.Free;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public List<User> Users { get; set; } = new();
    public List<Subscription> Subscriptions { get; set; } = new();
}