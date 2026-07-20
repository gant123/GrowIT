using System.Security.Claims;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Client.Controllers;

[ApiController]
[Route("bff/auth")]
public class BffAuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IAntiforgery _antiforgery;
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;

    public BffAuthController(
        ApplicationDbContext context,
        IAntiforgery antiforgery,
        SignInManager<User> signInManager,
        UserManager<User> userManager)
    {
        _context = context;
        _antiforgery = antiforgery;
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        return Ok(new AntiforgeryTokenResponse(tokens.RequestToken ?? string.Empty));
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-submit")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!IsSameOriginRequest())
        {
            return Forbid();
        }

        // 400 (not 403): the browser-side helper retries once on 400 with a fresh
        // antiforgery token, which recovers from a rotated cookie/token pair.
        if (!await IsValidAntiforgeryRequestAsync())
        {
            return BadRequest("The request could not be verified. Please try again.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail.ToUpperInvariant());

        if (user is null)
        {
            // Equalize timing with the found-user path (which runs PBKDF2 via CheckPasswordAsync)
            // so response time can't distinguish registered from unregistered emails.
            EqualizePasswordTiming(request.Password);
            return Unauthorized("Invalid email or password");
        }

        // Lockout is enforced before password verification so a locked account cannot be
        // brute-forced during the lockout window.
        if (await _userManager.IsLockedOutAsync(user))
        {
            // Structured code lets the sign-in page react to the state instead of matching message text.
            return StatusCode(StatusCodes.Status423Locked, new
            {
                code = "account-locked",
                message = "This account is temporarily locked after repeated failed sign-in attempts. Wait about 15 minutes and try again, or reset your password."
            });
        }

        // Verify the password BEFORE disclosing any other account state (deactivated,
        // unconfirmed). A wrong password always gets the same generic 401, so this
        // endpoint cannot be used to probe which emails have accounts.
        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return Unauthorized("Invalid email or password");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        if (!user.IsActive)
        {
            return Unauthorized("This account has been deactivated. Contact your organization administrator.");
        }

        if (!user.EmailConfirmed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "email-not-confirmed",
                message = "Your email address has not been confirmed yet."
            });
        }

        await _signInManager.SignInAsync(user, request.RememberMe);
        await RecordSignInEventAsync(user);
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!IsSameOriginRequest())
        {
            return Forbid();
        }

        if (!await IsValidAntiforgeryRequestAsync())
        {
            return BadRequest("The request could not be verified. Please try again.");
        }

        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        if (!(User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized();
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = User.FindFirst("tenantId")?.Value;

        return Ok(new
        {
            IsAuthenticated = true,
            UserId = userId,
            TenantId = tenantId,
            Email = User.FindFirst(ClaimTypes.Email)?.Value,
            Name = User.Identity?.Name,
            Role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value
        });
    }

    private async Task RecordSignInEventAsync(User user)
    {
        try
        {
            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = user.Email;
            }

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();
            if (string.IsNullOrWhiteSpace(userAgent))
            {
                userAgent = null;
            }

            _context.UserSignInEvents.Add(new UserSignInEvent
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TenantId = user.TenantId,
                Email = user.Email ?? string.Empty,
                DisplayName = displayName,
                ClientIp = clientIp,
                UserAgent = userAgent,
                OccurredAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
        catch
        {
            // Sign-in should not fail if telemetry persistence fails.
        }
    }

    // Cached dummy hash (produced by the app's real hasher, so its format matches and the verify
    // does the same PBKDF2 work) used to spend the same CPU on unknown emails as on real ones.
    private static string? _timingDummyHash;

    private void EqualizePasswordTiming(string? providedPassword)
    {
        var hasher = _userManager.PasswordHasher;
        var dummy = _timingDummyHash ??= hasher.HashPassword(new User(), "timing-equalization-not-a-real-password");
        hasher.VerifyHashedPassword(new User(), dummy, providedPassword ?? string.Empty);
    }

    private bool IsSameOriginRequest()
    {
        var requestHost = Request.Host;
        if (!requestHost.HasValue)
        {
            return false;
        }

        var origin = Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) &&
            Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return HostsMatch(requestHost, Request.Scheme, originUri);
        }

        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer) &&
            Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
        {
            return HostsMatch(requestHost, Request.Scheme, refererUri);
        }

        // Development tools and some non-browser clients may omit both headers.
        return HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
    }

    private static bool HostsMatch(HostString requestHost, string requestScheme, Uri source)
    {
        var sourcePort = source.IsDefaultPort
            ? (string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : source.Port;

        var requestPort = requestHost.Port
            ?? (string.Equals(requestScheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);

        if (!string.Equals(source.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return sourcePort == requestPort;
    }

    private async Task<bool> IsValidAntiforgeryRequestAsync()
    {
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private sealed record AntiforgeryTokenResponse(string Token);
}
