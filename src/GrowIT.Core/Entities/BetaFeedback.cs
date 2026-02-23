using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class BetaFeedback : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    public string Category { get; set; } = "Other";
    public string Severity { get; set; } = "Medium";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string Status { get; set; } = "Open";
    public string? AdminNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
