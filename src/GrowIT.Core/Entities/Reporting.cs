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

    public Guid? RequestedByUserId { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class ReportSchedule : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DateTime NextRun { get; set; }
    public bool IsActive { get; set; } = true;

    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
