using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GrowIT.Core.Entities;
using Microsoft.IdentityModel.Tokens;

namespace GrowIT.API.Services;

public class TokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public string CreateToken(User user, Guid tenantId)
    {
        // 1. Define the "Claims" (Data hidden inside the token)
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim("tenantId", tenantId.ToString()) // Crucial for Multi-tenancy!
        };

        if (!string.IsNullOrEmpty(user.Role))
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role));
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
}