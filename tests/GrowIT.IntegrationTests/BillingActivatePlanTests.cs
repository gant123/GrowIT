using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;

namespace GrowIT.Backend.Tests;

public class BillingActivatePlanTests
{
    // Activating a no-cost plan applies it directly (no Stripe needed) and the tenant's
    // usage limits immediately reflect the new plan.
    [Fact]
    public async Task ActivatePlan_SwitchesTenantToSelectedPlan_AndLimitsFollow()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        var planId = Guid.Empty;
        await factory.SeedAsync(db =>
        {
            var plan = new SubscriptionPlan { Name = "Growth", MaxUsers = 10, MaxClients = 500 };
            db.SubscriptionPlans.Add(plan);
            planId = plan.Id;
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var activate = await client.PostAsJsonAsync("/api/billing/activate-plan", new { planId });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var usage = await client.GetFromJsonAsync<PlanUsageDto>("/api/billing/usage");
        Assert.NotNull(usage);
        Assert.Equal("Growth", usage!.PlanName);
        Assert.Equal(500, usage.ClientsMax);
    }

    [Fact]
    public async Task ActivatePlan_ReturnsNotFound_ForUnknownPlan()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/billing/activate-plan", new { planId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivatePlan_IsForbidden_ForNonAdminRole()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        var planId = Guid.Empty;
        await factory.SeedAsync(db =>
        {
            var free = new SubscriptionPlan { Name = "Free", MaxUsers = 2, MaxClients = 25 };
            db.SubscriptionPlans.Add(free);
            planId = free.Id;
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Member");

        var response = await client.PostAsJsonAsync("/api/billing/activate-plan", new { planId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
