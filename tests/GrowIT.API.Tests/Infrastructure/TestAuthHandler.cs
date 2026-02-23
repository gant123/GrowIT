using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GrowIT.API.Tests.Infrastructure;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string TenantHeader = "X-Test-TenantId";
    public const string UserHeader = "X-Test-UserId";
    public const string RoleHeader = "X-Test-Role";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenantId = Request.Headers.TryGetValue(TenantHeader, out var tenantHeader) &&
                       Guid.TryParse(tenantHeader.ToString(), out var parsedTenant)
            ? parsedTenant
            : Guid.Parse("11111111-1111-1111-1111-111111111111");

        var userId = Request.Headers.TryGetValue(UserHeader, out var userHeader) &&
                     Guid.TryParse(userHeader.ToString(), out var parsedUser)
            ? parsedUser
            : Guid.Parse("22222222-2222-2222-2222-222222222222");

        var role = Request.Headers.TryGetValue(RoleHeader, out var roleHeader)
            ? roleHeader.ToString()
            : "Admin";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "integration-test-user"),
            new(ClaimTypes.Role, role),
            new("tenantId", tenantId.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
