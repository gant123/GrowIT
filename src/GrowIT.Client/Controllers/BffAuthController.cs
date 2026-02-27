using System.Security.Claims;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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

    public BffAuthController(ApplicationDbContext context, IAntiforgery antiforgery)
    {
        _context = context;
        _antiforgery = antiforgery;
    }

    [AllowAnonymous]
    [HttpGet("csrf")]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        return Ok(new { token = tokens.RequestToken });
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

        if (!await IsValidAntiforgeryRequestAsync())
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid email or password");
        }

        if (!user.IsActive)
        {
            return Unauthorized("This account has been deactivated. Contact your organization administrator.");
        }

        await SignInAsync(user, request.RememberMe);
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
            return Forbid();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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

    private async Task SignInAsync(User user, bool rememberMe)
    {
        var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = user.Email;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, displayName),
            new("tenantId", user.TenantId.ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
            claims.Add(new Claim("role", user.Role));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            AllowRefresh = true,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(rememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromHours(12))
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
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
                Email = user.Email,
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
}
