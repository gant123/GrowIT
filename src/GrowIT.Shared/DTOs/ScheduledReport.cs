namespace GrowIT.Shared.DTOs;

public class ScheduledReport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Frequency { get; set; } = "";
    public DateTime NextRun { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
