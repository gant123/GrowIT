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
    public string ReportType { get; set; } = "impact-summary";
    public string Format { get; set; } = "pdf";
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
}

public class UpdateScheduledReportRequest
{
    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = "impact-summary";
    public string Format { get; set; } = "pdf";
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
}

public class ReportRunDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastDownloadedAt { get; set; }
    public long? DurationMs { get; set; }

    public string? FiscalYear { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? GroupBy { get; set; }

    public string RequestPayloadJson { get; set; } = "{}";
    public List<ReportRunTimelineItemDto> Timeline { get; set; } = new();
    public List<ReportRunDownloadEventDto> DownloadEvents { get; set; } = new();
}

public class ReportRunTimelineItemDto
{
    public string EventType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? Details { get; set; }
}

public class ReportRunDownloadEventDto
{
    public Guid Id { get; set; }
    public DateTime DownloadedAt { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
}
