using System.Security.Claims;
using GrowIT.Core.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GrowIT.Backend.Services;

/// <summary>
/// Security-stamp validation for the auth cookie that works with tenant-filtered users.
///
/// The stock <see cref="SecurityStampValidator{TUser}"/> breaks this app in two ways:
/// 1. It loads the user through <c>UserManager.GetUserAsync</c>, which applies the tenant query
///    filter. That filter reads the tenant from <c>HttpContext.User</c>, which is still empty while
///    the cookie is being authenticated, so the lookup becomes <c>WHERE FALSE</c>, the user is
///    never found and every session is rejected once the validation interval has passed.
/// 2. On rejection it signs out <c>Identity.TwoFactorRememberMe</c>, a scheme AddIdentityCore does
///    not register, so the request dies with an exception (HTTP 500) instead of a clean sign-out.
/// </summary>
public sealed class GrowITSecurityStampValidator : SecurityStampValidator<User>
{
    public GrowITSecurityStampValidator(
        IOptions<SecurityStampValidatorOptions> options,
        SignInManager<User> signInManager,
        ILoggerFactory logger)
        : base(options, signInManager, logger)
    {
    }

    public override async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var issuedUtc = context.Properties.IssuedUtc;
        var isDue = issuedUtc is null ||
                    TimeProvider.GetUtcNow() - issuedUtc.Value > Options.ValidationInterval;
        if (!isDue)
        {
            return;
        }

        var user = await VerifySecurityStamp(context.Principal);
        if (user is not null)
        {
            // Re-issues the principal (fresh roles/claims) and renews the cookie.
            await SecurityStampVerified(user, context);
            return;
        }

        // Revoked session (password change, deactivation, role change): drop the cookie so the
        // request continues anonymously and is redirected to sign-in like any signed-out request.
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }

    protected override async Task<User?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var userManager = SignInManager.UserManager;
        if (!Guid.TryParse(userManager.GetUserId(principal), out var userId))
        {
            return null;
        }

        // Tenant context is not established yet at this point of the pipeline, so bypass the
        // tenant filter and look the user up by primary key only.
        var user = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null || !user.IsActive)
        {
            return null;
        }

        var stamp = principal.FindFirstValue(userManager.Options.ClaimsIdentity.SecurityStampClaimType);
        return await SignInManager.ValidateSecurityStampAsync(user, stamp) ? user : null;
    }
}
