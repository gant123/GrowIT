using System.Security.Claims;

namespace GrowIT.Backend.Services;

/// <summary>
/// Shared claim helpers so role checks stay in sync across controllers.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// True when the principal carries the platform-wide SuperAdmin role.
    /// Tolerates both the long (<see cref="ClaimTypes.Role"/>) and short ("role")
    /// claim types emitted by the JWT/cookie composite auth pipeline.
    /// </summary>
    public static bool IsSuperAdmin(this ClaimsPrincipal user) =>
        user.Claims.Any(c =>
            (c.Type == ClaimTypes.Role || c.Type == "role") &&
            string.Equals(c.Value?.Trim(), "SuperAdmin", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the principal carries any of the given roles (same matching rules as the
    /// "AdminOnly"/"AdminOrManager" policies).
    /// </summary>
    public static bool HasAnyRole(this ClaimsPrincipal user, params string[] roles) =>
        user.Claims.Any(c =>
            (c.Type == ClaimTypes.Role || c.Type == "role") &&
            !string.IsNullOrWhiteSpace(c.Value) &&
            roles.Any(r => string.Equals(c.Value.Trim(), r, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Same membership as the "AdminOnly" policy.</summary>
    public static bool IsTenantAdmin(this ClaimsPrincipal user) =>
        user.HasAnyRole("SuperAdmin", "Admin", "Owner");
}
