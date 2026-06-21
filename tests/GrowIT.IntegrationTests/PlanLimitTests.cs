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

    // Seeds a plan with the given user cap and fills "used seats" with pending invites
    // (PlanLimitService counts active users + pending invites against MaxUsers).
    private static Task SeedPlanAndInvitesAsync(GrowItApiFactory factory, Guid tenantId, int maxUsers, int pendingInvites) =>
        factory.SeedAsync(db =>
        {
            var plan = new SubscriptionPlan { Name = "Free", MaxUsers = maxUsers, MaxClients = 25 };
            db.SubscriptionPlans.Add(plan);
            db.Subscriptions.Add(new Subscription
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Active,
                StartDate = DateTime.UtcNow,
            });
            for (var i = 0; i < pendingInvites; i++)
            {
                db.OrganizationInvites.Add(new OrganizationInvite
                {
                    TenantId = tenantId,
                    Email = $"pending{i}@example.com",
                    Role = "Member",
                    TokenHash = $"hash{i}",
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                });
            }
            return Task.CompletedTask;
        });

    private static object NewInvite() => new
    {
        email = "newhire@example.com",
        firstName = "New",
        lastName = "Hire",
        role = "Member",
        expiresInDays = 7,
    };

    [Fact]
    public async Task CreateInvite_IsBlockedWhenAtUserLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndInvitesAsync(factory, tenantId, maxUsers: 1, pendingInvites: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/admin/invites", NewInvite());

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_IsAllowedWhenUnderUserLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndInvitesAsync(factory, tenantId, maxUsers: 5, pendingInvites: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/admin/invites", NewInvite());

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvite_SuperAdminBypassesUserLimit()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedPlanAndInvitesAsync(factory, tenantId, maxUsers: 1, pendingInvites: 1);
        using var client = factory.CreateTenantClient(tenantId, role: "SuperAdmin");

        var response = await client.PostAsJsonAsync("/api/admin/invites", NewInvite());

        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

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
