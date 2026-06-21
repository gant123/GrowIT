using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Tests;

public class PlanLimitTests
{
    private static Task SeedPlanAndClientsAsync(GrowItApiFactory factory, Guid tenantId, int maxClients, int existingClients) =>
        factory.SeedAsync(db =>
        {
            var plan = new SubscriptionPlan { Name = "Free", MaxUsers = 2, MaxClients = maxClients };
            db.SubscriptionPlans.Add(plan);
            db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.UtcNow,
            });
            for (var i = 0; i < existingClients; i++)
            {
                db.Clients.Add(new GrowIT.Core.Entities.Client { TenantId = tenantId, FirstName = "Existing", LastName = $"Client{i}" });
            }
            return Task.CompletedTask;
        });

    [Fact]
    public async Task CreateClient_IsBlockedWhenAtPlanLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndClientsAsync(factory, tenantId, maxClients: 1, existingClients: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/clients", new { firstName = "New", lastName = "Client" });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_IsAllowedWhenUnderPlanLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndClientsAsync(factory, tenantId, maxClients: 5, existingClients: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/clients", new { firstName = "New", lastName = "Client" });

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task CreateClient_SuperAdminBypassesPlanLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndClientsAsync(factory, tenantId, maxClients: 1, existingClients: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "SuperAdmin");

        var response = await client.PostAsJsonAsync("/api/clients", new { firstName = "New", lastName = "Client" });

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }
}
