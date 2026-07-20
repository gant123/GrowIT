using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrowIT.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GrowIT.Backend.Services;

public class TokenService
{
    private readonly IConfiguration _config;
    private readonly UserManager<User> _userManager;

    public TokenService(IConfiguration config, UserManager<User> userManager)
    {
        _config = config;
        _userManager = userManager;
    }

    public string CreateToken(User user, Guid tenantId, IEnumerable<string>? roles = null)
    {
        // 1. Define the "Claims" (Data hidden inside the token)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim("tenantId", tenantId.ToString()) // Crucial for Multi-tenancy!
        };

        foreach (var role in (roles ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var jwtKey = GetRequiredSetting("Jwt:Key");
        var jwtIssuer = GetRequiredSetting("Jwt:Issuer");
        var jwtAudience = GetRequiredSetting("Jwt:Audience");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Keep bearer tokens short-lived: the web app mints a fresh token per request from
        // the cookie session, so a long lifetime only extends how long a captured token
        // (or a deactivated user's token) keeps working. Configurable for API consumers.
        var lifetimeMinutes = int.TryParse(_config["Jwt:AccessTokenMinutes"], out var configuredMinutes)
            ? Math.Clamp(configuredMinutes, 5, 24 * 60)
            : 60;

        // 3. Build the Token
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(lifetimeMinutes),
            SigningCredentials = creds,
            Issuer = jwtIssuer,
            Audience = jwtAudience
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    public string? TryCreateToken(ClaimsPrincipal principal)
    {
        var userIdRaw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantIdRaw = principal.FindFirst("tenantId")?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        if (!Guid.TryParse(userIdRaw, out var userId) ||
            !Guid.TryParse(tenantIdRaw, out var tenantId) ||
            string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var user = new User
        {
            Id = userId,
            Email = email
        };

        return CreateToken(user, tenantId, roles);
    }

    public async Task<string?> TryCreateTokenAsync(ClaimsPrincipal principal)
    {
        var userIdRaw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return null;
        }

        // Always resolve the user from the database and re-check IsActive + current roles
        // before minting. This token is projected per /api request from the browser's cookie
        // principal, whose claims can be stale: a claims-only shortcut would let a deactivated
        // or role-demoted user keep full API access until their cookie expired. The extra
        // primary-key lookup is the price of enforcing revocation on every request.
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null || !user.IsActive || user.TenantId == Guid.Empty)
        {
            return null;
        }

        var identityRoles = await _userManager.GetRolesAsync(user);
        return CreateToken(user, user.TenantId, identityRoles);
    }

    public async Task<string> CreateTokenAsync(User user, Guid tenantId)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return CreateToken(user, tenantId, roles);
    }

    private string GetRequiredSetting(string key)
    {
        var value = _config[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Configuration value '{key}' is required.");
        }

        return value;
    }
}
