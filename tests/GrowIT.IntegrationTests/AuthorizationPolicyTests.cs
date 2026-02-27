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
}
