using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GrowIT.Backend.Services;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

/// <summary>
/// TryCreateTokenAsync projects the browser's cookie principal into a per-request bearer token
/// for internal /api calls. It must re-check the account against the database every time — never
/// trust the (possibly stale) cookie claims — or a deactivated / role-demoted user keeps full API
/// access until their cookie expires. These tests were the gap that let that fast-path ship: the
/// rest of the suite authenticates via TestAuthHandler and never exercises TokenService.
/// </summary>
public class TokenServiceTests
{
    [Fact]
    public async Task TryCreateTokenAsync_MintsFromDatabase_EvenWhenPrincipalClaimsAreEmpty()
    {
        using var factory = new GrowItApiFactory();
        var (userId, tenantId) = await SeedUserAsync(factory, isActive: true, role: "Admin");

        // Principal carries ONLY the user id — no tenant claim. A correct implementation must
        // still mint a valid token, sourcing the tenant from the database rather than the
        // (here empty) cookie claims.
        var principal = PrincipalWithIdOnly(userId);

        string? token;
        using (var scope = factory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
            token = await tokenService.TryCreateTokenAsync(principal);
        }

        Assert.False(string.IsNullOrWhiteSpace(token));

        // The tenant in the minted token comes from the DB user, not the (absent) principal claim.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "tenantId").Value);
    }

    [Fact]
    public async Task TryCreateTokenAsync_ReturnsNull_ForDeactivatedUser()
    {
        using var factory = new GrowItApiFactory();
        var (userId, _) = await SeedUserAsync(factory, isActive: false, role: "Admin");

        // A stale cookie for a now-deactivated user still carries a valid-looking role claim.
        var principal = PrincipalWithClaims(userId, tenantId: Guid.NewGuid(), role: "Admin");

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = await tokenService.TryCreateTokenAsync(principal);

        Assert.Null(token);
    }

    private static ClaimsPrincipal PrincipalWithIdOnly(Guid userId) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "TestCookie"));

    private static ClaimsPrincipal PrincipalWithClaims(Guid userId, Guid tenantId, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, "stale@example.test"),
            new Claim("tenantId", tenantId.ToString()),
            new Claim(ClaimTypes.Role, role)
        }, "TestCookie"));

    private static async Task<(Guid userId, Guid tenantId)> SeedUserAsync(GrowItApiFactory factory, bool isActive, string role)
    {
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db => { db.Tenants.Add(new Tenant { Id = tenantId, Name = "Token Test Org" }); return Task.CompletedTask; });

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        if (await roleManager.FindByNameAsync(role) is null)
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(role)
            {
                Id = Guid.NewGuid(),
                NormalizedName = role.ToUpperInvariant()
            });
            Assert.True(roleResult.Succeeded, string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }

        var email = $"token-{Guid.NewGuid():N}@example.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = "Token",
            LastName = "User",
            Email = email,
            UserName = email,
            IsActive = isActive,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await userManager.CreateAsync(user, "TokenUser2026!Pass");
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        var addRole = await userManager.AddToRoleAsync(user, role);
        Assert.True(addRole.Succeeded, string.Join(", ", addRole.Errors.Select(e => e.Description)));

        return (user.Id, tenantId);
    }
}
