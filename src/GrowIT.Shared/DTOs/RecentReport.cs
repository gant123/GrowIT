namespace GrowIT.Shared.DTOs;

public class RecentReport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
}
