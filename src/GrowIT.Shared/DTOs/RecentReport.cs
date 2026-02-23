namespace GrowIT.Shared.DTOs;

public class RecentReport
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string Status { get; set; } = "Generated";
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastDownloadedAt { get; set; }
    public DateTime GeneratedAt { get; set; }
}
