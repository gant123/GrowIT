namespace GrowIT.Core.Entities;

public class UnauthorizedAccessAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Path { get; set; } = "/";
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
}

public class UserSignInEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
