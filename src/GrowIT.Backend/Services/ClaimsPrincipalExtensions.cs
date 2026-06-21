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
}
