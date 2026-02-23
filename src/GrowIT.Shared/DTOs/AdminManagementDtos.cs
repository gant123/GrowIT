namespace GrowIT.Shared.DTOs;

public class OrganizationSettingsDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string OrganizationType { get; set; } = string.Empty;
    public string OrganizationSize { get; set; } = string.Empty;
    public bool TrackPeople { get; set; }
    public bool TrackInvestments { get; set; }
    public bool TrackOutcomes { get; set; }
    public bool TrackPrograms { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateOrganizationSettingsRequest
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string OrganizationType { get; set; } = string.Empty;
    public string OrganizationSize { get; set; } = string.Empty;
    public bool TrackPeople { get; set; }
    public bool TrackInvestments { get; set; }
    public bool TrackOutcomes { get; set; }
    public bool TrackPrograms { get; set; }
}

public class AdminUserListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? DeactivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateAdminUserRoleRequest
{
    public string Role { get; set; } = string.Empty;
}

public class OrganizationInviteListItemDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public string? InvitedByName { get; set; }
}

public class CreateOrganizationInviteRequest
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public int ExpiresInDays { get; set; } = 7;
}

public class CreateOrganizationInviteResponse
{
    public Guid InviteId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string InviteLink { get; set; } = string.Empty;
}

public class InviteAuditNotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SeedDemoDataResponseDto
{
    public string Message { get; set; } = string.Empty;
    public int ProgramsCreated { get; set; }
    public int FundsCreated { get; set; }
    public int HouseholdsCreated { get; set; }
    public int ClientsCreated { get; set; }
    public int FamilyMembersCreated { get; set; }
    public int InvestmentsCreated { get; set; }
    public int ImprintsCreated { get; set; }
    public int GrowthPlansCreated { get; set; }
}

public class InviteValidationDto
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public class AcceptInviteRequest
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
