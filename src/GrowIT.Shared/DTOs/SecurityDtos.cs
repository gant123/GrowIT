namespace GrowIT.Shared.DTOs;

public class SecurityAccessAttemptQueryParams
{
    public string? Ip { get; set; }
    public int? Take { get; set; }
}

public class SecurityKnownUserMatchDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
}

public class SecurityAccessAttemptDto
{
    public Guid Id { get; set; }
    public string Path { get; set; } = "/";
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    public DateTime OccurredAt { get; set; }
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public List<SecurityKnownUserMatchDto> KnownUsers { get; set; } = [];
}
