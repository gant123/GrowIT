using GrowIT.Core.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace GrowIT.Core.Entities;

public class User : IdentityUser<Guid>, IMustHaveTenant
{
    public Guid TenantId { get; set; }
    
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    public DateTime? DeactivatedAt { get; set; }
    public string? PhotoUrl { get; set; }
    public bool NotifyInviteActivity { get; set; } = true;
    public bool NotifySystemAlerts { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
