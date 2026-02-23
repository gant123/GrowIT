using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class OrganizationInvite : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Guid? InvitedByUserId { get; set; }
    public Guid? AcceptedUserId { get; set; }

    public bool IsAccepted => AcceptedAt.HasValue;
    public bool IsRevoked => RevokedAt.HasValue;
}
