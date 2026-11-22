using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class CreateGrowthPlanRequest
{
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; } = Season.Planting;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? TargetEndDate { get; set; }

    /// <summary>Team member who owns the plan (an active user in this organization), or none.</summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>How many goals the plan has. Progress is goals completed out of this number.</summary>
    [Range(0, GrowthPlanLimits.MaxGoals, ErrorMessage = "Use between 0 and 100 goals.")]
    public int TotalGoals { get; set; }
}

public class UpdateGrowthPlanRequest
{
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public GrowthPlanStatus Status { get; set; }
    public DateTime? TargetEndDate { get; set; }

    /// <summary>Team member who owns the plan; null leaves it unassigned.</summary>
    public Guid? AssignedToUserId { get; set; }

    /// <summary>Null keeps the current number.</summary>
    [Range(0, GrowthPlanLimits.MaxGoals, ErrorMessage = "Use between 0 and 100 goals.")]
    public int? TotalGoals { get; set; }

    /// <summary>Null keeps the current number.</summary>
    [Range(0, GrowthPlanLimits.MaxGoals, ErrorMessage = "Goals completed must be between 0 and 100.")]
    public int? CompletedGoals { get; set; }
}

/// <summary>Record progress on a plan without re-sending the rest of it.</summary>
public class UpdateGrowthPlanProgressRequest
{
    [Range(0, GrowthPlanLimits.MaxGoals, ErrorMessage = "Goals completed must be between 0 and 100.")]
    public int CompletedGoals { get; set; }

    /// <summary>Null keeps the current number of goals.</summary>
    [Range(0, GrowthPlanLimits.MaxGoals, ErrorMessage = "Use between 0 and 100 goals.")]
    public int? TotalGoals { get; set; }
}

public static class GrowthPlanLimits
{
    public const int MaxGoals = 100;
}
