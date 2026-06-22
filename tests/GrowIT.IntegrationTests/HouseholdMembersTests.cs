using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Tests;

public class HouseholdMembersTests
{
    // A household's member list/count should include the intake family members
    // (spouse/children) recorded against its clients, matching the case file view.
    [Fact]
    public async Task GetHouseholds_IncludesIntakeFamilyMembers_MatchingTheCaseFile()
    {
        var tenantId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        var headId = Guid.NewGuid();

        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Households.Add(new Household
            {
                Id = householdId,
                TenantId = tenantId,
                Name = "The Reyes Family",
                PrimaryClientId = headId
            });
            db.Clients.Add(new GrowIT.Core.Entities.Client
            {
                Id = headId,
                TenantId = tenantId,
                FirstName = "James",
                LastName = "Reyes",
                HouseholdId = householdId,
                HouseholdRole = HouseholdRole.Head
            });
            db.FamilyMembers.Add(new FamilyMember
            {
                TenantId = tenantId,
                ClientId = headId,
                FirstName = "Oma",
                LastName = "Reyes",
                Relationship = "Spouse"
            });
            return Task.CompletedTask;
        });
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var households = await client.GetFromJsonAsync<List<HouseholdDto>>("/api/households");

        Assert.NotNull(households);
        var reyes = Assert.Single(households!);
        Assert.Equal(2, reyes.MemberCount);
        Assert.Contains(reyes.Members, m => m.Name == "James Reyes" && m.IsCaseFile);
        Assert.Contains(reyes.Members, m => m.Name == "Oma Reyes" && !m.IsCaseFile);
    }
}
