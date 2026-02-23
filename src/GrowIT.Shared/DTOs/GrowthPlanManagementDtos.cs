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
}

public class UpdateGrowthPlanRequest
{
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public GrowthPlanStatus Status { get; set; }
    public DateTime? TargetEndDate { get; set; }
}
