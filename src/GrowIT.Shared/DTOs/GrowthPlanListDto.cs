using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class GrowthPlanListDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public GrowthPlanStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? TargetEndDate { get; set; }
    public int CompletedGoals { get; set; }
    public int TotalGoals { get; set; }
    public decimal ProgressPercentage { get; set; }
    public string? AssignedToUserName { get; set; }
}
