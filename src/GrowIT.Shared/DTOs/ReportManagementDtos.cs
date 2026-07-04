namespace GrowIT.Shared.DTOs;

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public static class ReportContract
{
    public const string ImpactSummary = "impact-summary";
    public const string InvestmentDetail = "investment-detail";
    public const string Investments = "investments";
    public const string OutcomesByCategory = "outcomes-by-category";
    public const string FundingUtilization = "funding-utilization";
    public const string SeasonProgression = "season-progression";
    public const string DemographicBreakdown = "demographic-breakdown";
    public const string CustomReport = "custom-report";

    public static readonly string[] SupportedReportTypes =
    [
        ImpactSummary,
        InvestmentDetail,
        Investments,
        OutcomesByCategory,
        FundingUtilization,
        SeasonProgression,
        DemographicBreakdown,
        CustomReport
    ];

    public static readonly string[] SupportedFormats = ["pdf", "excel", "csv"];
    public static readonly string[] SupportedFrequencies = ["Daily", "Weekly", "Monthly", "Quarterly", "Annual"];
    public static readonly string[] SupportedStatuses = ["Queued", "Generated", "Failed"];
}

public class GenerateReportRequest : IValidatableObject
{
    [MaxLength(80)]
    public string ReportType { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Format { get; set; } = "pdf";

    [MaxLength(20)]
    public string? FiscalYear { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    [MaxLength(80)]
    public string? GroupBy { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsSupported(ReportType, ReportContract.SupportedReportTypes, allowBlank: true))
            yield return new ValidationResult("Unsupported report type.", new[] { nameof(ReportType) });

        if (!IsSupported(Format, ReportContract.SupportedFormats, allowBlank: true))
            yield return new ValidationResult("Unsupported report format.", new[] { nameof(Format) });

        if (DateFrom.HasValue && DateTo.HasValue && DateFrom.Value.Date > DateTo.Value.Date)
            yield return new ValidationResult("Start date must be before end date.", new[] { nameof(DateFrom), nameof(DateTo) });
    }

    private static bool IsSupported(string? value, IEnumerable<string> allowed, bool allowBlank = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return allowBlank;

        return allowed.Any(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

public class RecentReportsQueryParams
{
    [MaxLength(200)]
    public string? Search { get; set; }

    [MaxLength(80)]
    public string? ReportType { get; set; }

    [MaxLength(20)]
    public string? Format { get; set; }

    [MaxLength(20)]
    public string? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    [Range(1, 1000)]
    public int? Take { get; set; }
}

public class ScheduledReportsQueryParams
{
    [MaxLength(200)]
    public string? Search { get; set; }

    [MaxLength(20)]
    public string? Frequency { get; set; }
    public bool IncludeInactive { get; set; }

    [Range(1, 1000)]
    public int? Take { get; set; }
}

public class CreateScheduledReportRequest : IValidatableObject
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string ReportType { get; set; } = "impact-summary";

    [Required]
    [MaxLength(20)]
    public string Format { get; set; } = "pdf";

    [Required]
    [MaxLength(20)]
    public string Frequency { get; set; } = string.Empty;

    public DateTime NextRun { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in ReportScheduleValidation.Validate(Name, ReportType, Format, Frequency, NextRun))
            yield return error;
    }
}

public class UpdateScheduledReportRequest : IValidatableObject
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string ReportType { get; set; } = "impact-summary";

    [Required]
    [MaxLength(20)]
    public string Format { get; set; } = "pdf";

    [Required]
    [MaxLength(20)]
    public string Frequency { get; set; } = string.Empty;

    public DateTime NextRun { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in ReportScheduleValidation.Validate(Name, ReportType, Format, Frequency, NextRun))
            yield return error;
    }
}

internal static class ReportScheduleValidation
{
    public static IEnumerable<ValidationResult> Validate(string name, string reportType, string format, string frequency, DateTime nextRun)
    {
        if (!IsSupported(reportType, ReportContract.SupportedReportTypes))
            yield return new ValidationResult("Unsupported report type.", new[] { nameof(CreateScheduledReportRequest.ReportType) });

        if (!IsSupported(format, ReportContract.SupportedFormats))
            yield return new ValidationResult("Unsupported report format.", new[] { nameof(CreateScheduledReportRequest.Format) });

        if (!IsSupported(frequency, ReportContract.SupportedFrequencies))
            yield return new ValidationResult("Unsupported schedule frequency.", new[] { nameof(CreateScheduledReportRequest.Frequency) });

        if (nextRun == default)
            yield return new ValidationResult("Next run date is required.", new[] { nameof(CreateScheduledReportRequest.NextRun) });
    }

    private static bool IsSupported(string? value, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return allowed.Any(item => string.Equals(item, value.Trim(), StringComparison.OrdinalIgnoreCase));
    }
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

    [JsonIgnore]
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
