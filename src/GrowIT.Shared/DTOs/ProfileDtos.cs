namespace GrowIT.Shared.DTOs;

public class UserProfileDto
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? PhotoUrl { get; set; }
    public bool NotifyInviteActivity { get; set; }
    public bool NotifySystemAlerts { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? OrganizationName { get; set; }
}

public class UpdateUserProfileRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateNotificationPreferencesRequest
{
    public bool NotifyInviteActivity { get; set; }
    public bool NotifySystemAlerts { get; set; }
}
