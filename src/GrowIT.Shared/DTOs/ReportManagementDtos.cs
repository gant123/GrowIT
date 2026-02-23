namespace GrowIT.Shared.DTOs;

public class GenerateReportRequest
{
    public string ReportType { get; set; } = string.Empty;
    public string? Format { get; set; } = "pdf";
    public string? FiscalYear { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? GroupBy { get; set; }
}

public class RecentReportsQueryParams
{
    public string? Search { get; set; }
    public string? ReportType { get; set; }
    public string? Format { get; set; }
    public string? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? Take { get; set; }
}

public class ScheduledReportsQueryParams
{
    public string? Search { get; set; }
    public string? Frequency { get; set; }
    public bool IncludeInactive { get; set; }
    public int? Take { get; set; }
}

public class CreateScheduledReportRequest
{
    public string Name { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
}

public class UpdateScheduledReportRequest
{
    public string Name { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
}
