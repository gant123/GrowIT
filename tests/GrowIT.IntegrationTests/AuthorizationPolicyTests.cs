using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;

namespace GrowIT.Backend.Tests;

public class AuthorizationPolicyTests
{
    [Fact]
    public async Task AdminOnly_SeedDemoData_RejectsManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Manager");

        var response = await client.PostAsJsonAsync("/api/admin/seed-demo-data", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_GrowthPlansCreate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/growthplans", new
        {
            personId = Guid.NewGuid(),
            title = "Test Plan",
            season = "Q1"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_ImprintCreate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/imprints", new
        {
            clientId = Guid.NewGuid(),
            title = "Outcome",
            category = 0,
            outcome = 0,
            dateOccurred = DateTime.UtcNow,
            notes = "Test"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOrManager_InvestmentApprove_RejectsCaseManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Case Manager");

        var response = await client.PostAsJsonAsync($"/api/investments/{Guid.NewGuid()}/approve", new
        {
            approvedBy = "tester"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOrManager_ReportsGenerate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/reports/generate", new
        {
            reportType = "readiness",
            format = "pdf"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_EmailDiagnostics_RejectsAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.GetAsync("/api/admin/email-diagnostics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_EmailDiagnostics_AllowsSuperAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.GetAsync("/api/admin/email-diagnostics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_SiteContent_RejectsOwner()
    {
        // Owner is a per-tenant role and must NOT inherit platform/site-wide SuperAdmin access.
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Owner");

        var response = await client.GetAsync("/api/admin/content/blog");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRole_RejectsEscalationToSuperAdminByAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new
        {
            role = "SuperAdmin"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRole_AllowsSuperAdminToGrantElevatedRole()
    {
        // A SuperAdmin passes the role-assignment gate; the request then fails only because
        // the target user does not exist (404), proving the elevated role was permitted.
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new
        {
            role = "SuperAdmin"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
