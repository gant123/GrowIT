namespace GrowIT.API.Services;

public class ReportSchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 60;
    public int MaxSchedulesPerCycle { get; set; } = 25;
    public string DefaultReportType { get; set; } = "impact-summary";
    public string DefaultFormat { get; set; } = "pdf";
}
