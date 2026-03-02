using System.Security.Claims;
using GrowIT.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GrowIT.Backend.Services;

public sealed class GrowITUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<User, IdentityRole<Guid>>
{
    public GrowITUserClaimsPrincipalFactory(
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.TenantId != Guid.Empty &&
            !identity.HasClaim(c => c.Type == "tenantId"))
        {
            identity.AddClaim(new Claim("tenantId", user.TenantId.ToString()));
        }

        var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            identity.RemoveClaimIfExists(ClaimTypes.Name);
            identity.AddClaim(new Claim(ClaimTypes.Name, displayName));
        }

        if (!string.IsNullOrWhiteSpace(user.Role) &&
            !identity.HasClaim(c => c.Type == "role" && string.Equals(c.Value, user.Role, StringComparison.OrdinalIgnoreCase)))
        {
            identity.AddClaim(new Claim("role", user.Role));
        }

        return identity;
    }
}

internal static class ClaimsIdentityExtensions
{
    public static void RemoveClaimIfExists(this ClaimsIdentity identity, string claimType)
    {
        var existing = identity.FindFirst(claimType);
        if (existing is not null)
        {
            identity.RemoveClaim(existing);
        }
    }
}
