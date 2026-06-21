using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;

namespace GrowIT.Backend.Tests;

/// <summary>
/// One data-driven check across every role and a representative endpoint for each
/// authorization policy. Replaces having to invite users for each role and click
/// through the app by hand. A role is considered to "pass" an endpoint when the
/// response is anything other than 403 Forbidden (the API authorizes before model
/// binding, so allowed-but-incomplete requests come back as 400/404/501 — never 403).
/// </summary>
public class RoleMatrixTests
{
    private static readonly string[] AllRoles =
        { "SuperAdmin", "Owner", "Admin", "Manager", "Case Manager", "Analyst", "Member" };

    private static HashSet<string> Roles(params string[] roles) =>
        new(roles, StringComparer.OrdinalIgnoreCase);

    private sealed record Endpoint(
        string Name,
        HashSet<string> AllowedRoles,
        Func<HttpClient, Task<HttpResponseMessage>> Send);

    [Fact]
    public async Task RoleMatrix_EnforcesExpectedAccessAcrossPolicies()
    {
        // Allowed-role sets per policy (SuperAdmin is a superset of every lower tier).
        var superAdminOnly = Roles("SuperAdmin");
        var adminOnly = Roles("SuperAdmin", "Admin", "Owner");
        var adminOrManager = Roles("SuperAdmin", "Admin", "Manager", "Owner");
        var serviceWriter = Roles("SuperAdmin", "Admin", "Manager", "Owner", "Case Manager");
        var anyAuthenticated = Roles(AllRoles);

        var endpoints = new[]
        {
            // SuperAdminOnly
            new Endpoint("GET /api/admin/email-diagnostics", superAdminOnly,
                c => c.GetAsync("/api/admin/email-diagnostics")),
            new Endpoint("GET /api/admin/feedback (review)", superAdminOnly,
                c => c.GetAsync("/api/admin/feedback")),
            new Endpoint("GET /api/admin/content/blog (site content)", superAdminOnly,
                c => c.GetAsync("/api/admin/content/blog")),

            // AdminOnly (Manager intentionally excluded)
            new Endpoint("GET /api/admin/users", adminOnly,
                c => c.GetAsync("/api/admin/users")),
            new Endpoint("POST /api/admin/seed-demo-data", adminOnly,
                c => c.PostAsJsonAsync("/api/admin/seed-demo-data", new { })),

            // AdminOrManager
            new Endpoint("POST /api/reports/generate", adminOrManager,
                c => c.PostAsJsonAsync("/api/reports/generate", new { reportType = "readiness", format = "pdf" })),
            new Endpoint("POST /api/investments/{id}/approve", adminOrManager,
                c => c.PostAsJsonAsync($"/api/investments/{Guid.NewGuid()}/approve", new { approvedBy = "matrix" })),

            // ServiceWriter
            new Endpoint("POST /api/clients", serviceWriter,
                c => c.PostAsJsonAsync("/api/clients", new { firstName = "A", lastName = "B" })),
            new Endpoint("POST /api/households", serviceWriter,
                c => c.PostAsJsonAsync("/api/households", new { name = "Matrix Household" })),
            new Endpoint("POST /api/growthplans", serviceWriter,
                c => c.PostAsJsonAsync("/api/growthplans", new { personId = Guid.NewGuid(), title = "Plan", season = "Q1" })),

            // Any authenticated user
            new Endpoint("GET /api/profile", anyAuthenticated,
                c => c.GetAsync("/api/profile")),
        };

        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var failures = new List<string>();

        foreach (var role in AllRoles)
        {
            using var client = factory.CreateTenantClient(tenantId, role: role);
            foreach (var endpoint in endpoints)
            {
                using var response = await endpoint.Send(client);
                var passedAuthorization = response.StatusCode != HttpStatusCode.Forbidden;
                var shouldBeAllowed = endpoint.AllowedRoles.Contains(role);

                if (passedAuthorization != shouldBeAllowed)
                {
                    failures.Add(
                        $"[{role}] {endpoint.Name}: expected {(shouldBeAllowed ? "ALLOW" : "DENY")} " +
                        $"but got {(int)response.StatusCode} {response.StatusCode}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} role/endpoint authorization mismatch(es):\n" + string.Join("\n", failures));
    }
}
