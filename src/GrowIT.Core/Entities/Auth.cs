using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class User : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Added to store initial user role
    
    public string? PasswordResetToken { get; set; }
    public DateTime? ResetTokenExpires { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Role : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UserRole : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}