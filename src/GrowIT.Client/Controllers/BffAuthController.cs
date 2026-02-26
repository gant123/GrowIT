using System.Security.Claims;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Client.Controllers;

[ApiController]
[Route("bff/auth")]
public class BffAuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public BffAuthController(ApplicationDbContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
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
        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
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
}
