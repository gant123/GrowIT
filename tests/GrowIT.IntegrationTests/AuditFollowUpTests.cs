using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

// Regression tests for fixes from the Nov 2026 blind browser pass.
public class AuditFollowUpTests
{
    [Fact]
    public async Task Insights_RejectsCaseManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Case Manager");

        var response = await client.GetAsync("/api/dashboard/insights");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Insights_NoRunwayEstimateWithoutSpendingHistory()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "Manager");

        var response = await client.GetAsync("/api/dashboard/insights");

        response.EnsureSuccessStatusCode();
        var insights = await response.Content.ReadFromJsonAsync<InsightsDto>();
        Assert.NotNull(insights);
        Assert.False(insights!.FundingReadiness.HasSpendingHistory);
        Assert.Equal(0, insights.FundingReadiness.EstimatedRunwayMonths);
    }

    [Theory]
    [InlineData("/api/billing/invoices")]
    [InlineData("/api/billing/payments")]
    [InlineData("/api/billing/events")]
    public async Task BillingHistory_RejectsCaseManager(string url)
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Case Manager");

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BillingOverview_OmitsHistoryForCaseManager()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Invoices.Add(new Invoice { TenantId = tenantId, InvoiceNumber = "INV-1", AmountDue = 49m, DueDate = DateTime.UtcNow });
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Case Manager");

        var response = await client.GetAsync("/api/billing/overview");

        response.EnsureSuccessStatusCode();
        var overview = await response.Content.ReadFromJsonAsync<BillingOverviewDto>();
        Assert.NotNull(overview);
        Assert.Empty(overview!.Invoices);
        Assert.Empty(overview.Payments);
        Assert.Empty(overview.Events);
    }

    [Fact]
    public async Task CreateInvite_StoresRoleWithCanonicalSpelling()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            var plan = new SubscriptionPlan { Name = "Free", MaxUsers = 10, MaxClients = 25 };
            db.SubscriptionPlans.Add(plan);
            db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.UtcNow,
            });
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Hope Works" });
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/admin/invites", new
        {
            email = "casey@example.com",
            firstName = "Casey",
            lastName = "Rivera",
            role = "case manager",
            expiresInDays = 7,
        });

        response.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var invite = await db.OrganizationInvites.IgnoreQueryFilters()
            .SingleAsync(i => i.TenantId == tenantId && i.Email == "casey@example.com");
        Assert.Equal("Case Manager", invite.Role);
    }

    [Fact]
    public async Task CreateInvite_UnknownRoleListsValidChoices()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.PostAsJsonAsync("/api/admin/invites", new
        {
            email = "someone@example.com",
            firstName = "Some",
            lastName = "One",
            role = "Wizard",
            expiresInDays = 7,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Case Manager", body);
    }

    [Fact]
    public async Task GenerateReport_UsesReadableNameInRequesterTimeZone()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/reports/generate", new GenerateReportRequest
        {
            ReportType = ReportContract.ImpactSummary,
            Format = "pdf",
            TimeZone = "America/Chicago"
        });

        response.EnsureSuccessStatusCode();
        var report = await response.Content.ReadFromJsonAsync<RecentReport>();
        Assert.NotNull(report);
        Assert.StartsWith("Impact Summary - ", report!.Name);
        Assert.DoesNotContain("UTC", report.Name);
    }
}
