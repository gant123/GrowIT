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

        foreach (var role in (roles ?? RolesFromUser(user)).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // 2. Create the Security Key (We will add this to appsettings.json next)
        var jwtKey = _config["Jwt:Key"] ?? "ThisIsMySuperSecretKeyForGrowITLocalDevelopment123!";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 3. Build the Token
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7), // Token lasts 1 week
            SigningCredentials = creds,
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"]
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

        var role = roles.FirstOrDefault() ?? string.Empty;
        var user = new User
        {
            Id = userId,
            Email = email,
            Role = role
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

        var tenantIdRaw = principal.FindFirst("tenantId")?.Value;
        var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
        var roles = principal.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Guid.TryParse(tenantIdRaw, out var tenantId) &&
            !string.IsNullOrWhiteSpace(email) &&
            roles.Count > 0)
        {
            var principalUser = new User
            {
                Id = userId,
                Email = email,
                Role = roles.First()
            };

            return CreateToken(principalUser, tenantId, roles);
        }

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

    private static IEnumerable<string> RolesFromUser(User user)
    {
        if (!string.IsNullOrWhiteSpace(user.Role))
        {
            yield return user.Role;
        }
    }
}
