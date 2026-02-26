namespace GrowIT.Backend.Services;

public class ReportSchedulerState
{
    public bool Enabled { get; set; }
    public bool IsRunningCycle { get; set; }
    public DateTime? LastCycleStartedAtUtc { get; set; }
    public DateTime? LastCycleCompletedAtUtc { get; set; }
    public int LastProcessedSchedules { get; set; }
    public string? LastError { get; set; }
}
