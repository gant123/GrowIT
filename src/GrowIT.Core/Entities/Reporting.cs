using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class ReportRun : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = "pdf";
    public string ReportType { get; set; } = string.Empty;
    public string RequestPayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Queued";
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastDownloadedAt { get; set; }

    public Guid? RequestedByUserId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class ReportSchedule : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string ReportType { get; set; } = "impact-summary";
    public string Format { get; set; } = "pdf";
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class ReportRunDownloadEvent : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid ReportRunId { get; set; }
    public Guid? DownloadedByUserId { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
}
