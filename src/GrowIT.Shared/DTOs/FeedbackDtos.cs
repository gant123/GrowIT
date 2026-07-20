using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateBetaFeedbackRequest
{
    [StringLength(40)]
    public string Category { get; set; } = "Other";
    [StringLength(20)]
    public string Severity { get; set; } = "Medium";
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [Required, StringLength(5000)]
    public string Message { get; set; } = string.Empty;
    [StringLength(2048)]
    public string? PageUrl { get; set; }
}

public class BetaFeedbackListItemDto
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AdminNotes { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid? UserId { get; set; }
    public string? SubmittedByName { get; set; }
    public string? SubmittedByEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class BetaFeedbackQueryParams
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Category { get; set; }
    public string? Severity { get; set; }
    public int? Take { get; set; }
}

public class UpdateBetaFeedbackStatusRequest
{
    public string Status { get; set; } = "Open";
    public string? AdminNotes { get; set; }
}
