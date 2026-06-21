using System.Collections.Generic;
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

    // The demo path: a PAID plan activates directly when Stripe is not configured,
    // and the tenant's limits follow.
    [Fact]
    public async Task ActivatePlan_ActivatesPaidPlanDirectly_WhenStripeNotConfigured()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        var planId = Guid.Empty;
        await factory.SeedAsync(db =>
        {
            var pro = new SubscriptionPlan { Name = "Pro", MaxUsers = 10, MaxClients = 500, PriceMonthly = 49, PriceYearly = 490 };
            db.SubscriptionPlans.Add(pro);
            planId = pro.Id;
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var activate = await client.PostAsJsonAsync("/api/billing/activate-plan", new { planId });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var usage = await client.GetFromJsonAsync<PlanUsageDto>("/api/billing/usage");
        Assert.NotNull(usage);
        Assert.Equal("Pro", usage!.PlanName);
        Assert.Equal(500, usage.ClientsMax);
    }

    // The production guard: a PAID plan cannot be activated directly when Stripe IS
    // configured — it must go through checkout.
    [Fact]
    public async Task ActivatePlan_RequiresCheckout_ForPaidPlan_WhenStripeConfigured()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_dummy",
            ["Stripe:WebhookSecret"] = "whsec_dummy"
        });
        var planId = Guid.Empty;
        await factory.SeedAsync(db =>
        {
            var pro = new SubscriptionPlan { Name = "Pro", MaxUsers = 10, MaxClients = 500, PriceMonthly = 49, PriceYearly = 490 };
            db.SubscriptionPlans.Add(pro);
            planId = pro.Id;
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/billing/activate-plan", new { planId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
