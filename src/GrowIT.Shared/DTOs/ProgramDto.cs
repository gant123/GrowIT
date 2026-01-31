namespace GrowIT.Shared.DTOs;

public class ProgramDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DefaultUnitCost { get; set; }
    public int? CapacityLimit { get; set; }
    public string? CapacityPeriod { get; set; }
}
