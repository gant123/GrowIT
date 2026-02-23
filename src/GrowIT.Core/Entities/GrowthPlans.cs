using GrowIT.Core.Interfaces;
using GrowIT.Shared.Enums;

namespace GrowIT.Core.Entities;

public class GrowthPlan : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }
    public Client? Client { get; set; }

    public Guid? FamilyMemberId { get; set; }
    public FamilyMember? FamilyMember { get; set; }

    public Guid? AssignedToUserId { get; set; }
    public User? AssignedToUser { get; set; }

    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; } = Season.Planting;
    public GrowthPlanStatus Status { get; set; } = GrowthPlanStatus.Active;

    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? TargetEndDate { get; set; }

    public int CompletedGoals { get; set; }
    public int TotalGoals { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
