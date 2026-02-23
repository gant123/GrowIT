using GrowIT.API.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.API.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task Clients_GetAll_ReturnsOnlyCurrentTenantRecords()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantA,
                    FirstName = "Alice",
                    LastName = "TenantA",
                    Email = "alice@a.test",
                    Phone = "111",
                    HouseholdCount = 1,
                    StabilityScore = 5,
                    LifePhase = LifePhase.Crisis
                },
                new Client
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantB,
                    FirstName = "Bob",
                    LastName = "TenantB",
                    Email = "bob@b.test",
                    Phone = "222",
                    HouseholdCount = 1,
                    StabilityScore = 6,
                    LifePhase = LifePhase.Stable
                });

            return Task.CompletedTask;
        });

        using var clientA = factory.CreateTenantClient(tenantA);
        var responseA = await clientA.GetAsync("/api/clients");
        responseA.EnsureSuccessStatusCode();
        var clientsA = await responseA.ReadRequiredJsonAsync<List<ClientDto>>();

        Assert.Single(clientsA);
        Assert.Contains("Alice", clientsA[0].Name);

        using var clientB = factory.CreateTenantClient(tenantB);
        var responseB = await clientB.GetAsync("/api/clients");
        responseB.EnsureSuccessStatusCode();
        var clientsB = await responseB.ReadRequiredJsonAsync<List<ClientDto>>();

        Assert.Single(clientsB);
        Assert.Contains("Bob", clientsB[0].Name);
    }

    [Fact]
    public async Task MemberProfile_IsNotVisibleAcrossTenants()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                TenantId = tenantA,
                FirstName = "Casey",
                LastName = "Parent",
                Email = "casey@test.local",
                Phone = "555",
                HouseholdCount = 2,
                StabilityScore = 7,
                LifePhase = LifePhase.Thriving
            });

            db.FamilyMembers.Add(new FamilyMember
            {
                Id = memberId,
                TenantId = tenantA,
                ClientId = clientId,
                FirstName = "Jamie",
                LastName = "Child",
                Relationship = "Child"
            });

            return Task.CompletedTask;
        });

        using var otherTenantClient = factory.CreateTenantClient(tenantB);
        var response = await otherTenantClient.GetAsync($"/api/clients/members/{memberId}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
