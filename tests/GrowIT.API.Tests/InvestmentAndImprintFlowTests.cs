using System.Net;
using System.Net.Http.Json;
using GrowIT.API.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.API.Tests;

public class InvestmentAndImprintFlowTests
{
    [Fact]
    public async Task Investment_CreateThenDelete_UpdatesFundBalanceCorrectly()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var programId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Taylor",
                LastName = "Client",
                Email = "taylor@test.local",
                Phone = "555",
                HouseholdCount = 1,
                StabilityScore = 4,
                LifePhase = LifePhase.Crisis
            });

            db.Funds.Add(new Fund
            {
                Id = fundId,
                TenantId = tenantId,
                Name = "General Relief",
                TotalAmount = 1000m,
                AvailableAmount = 1000m
            });

            db.Programs.Add(new GrowIT.Core.Entities.Program
            {
                Id = programId,
                TenantId = tenantId,
                Name = "Rent Assistance",
                Description = "Emergency rent support",
                DefaultUnitCost = 200m
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);

        var createResponse = await client.PostAsJsonAsync("/api/investments", new CreateInvestmentRequest
        {
            ClientId = clientId,
            FundId = fundId,
            ProgramId = programId,
            Amount = 200m,
            PayeeName = "Landlord LLC",
            Reason = "Emergency rent payment"
        });

        createResponse.EnsureSuccessStatusCode();
        var investmentId = await createResponse.ReadGuidPropertyAsync("InvestmentId");

        var fundsAfterCreate = await (await client.GetAsync("/api/financials/funds"))
            .ReadRequiredJsonAsync<List<FundDto>>();
        Assert.Single(fundsAfterCreate);
        Assert.Equal(800m, fundsAfterCreate[0].AvailableAmount);

        var deleteResponse = await client.DeleteAsync($"/api/investments/{investmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var fundsAfterDelete = await (await client.GetAsync("/api/financials/funds"))
            .ReadRequiredJsonAsync<List<FundDto>>();
        Assert.Single(fundsAfterDelete);
        Assert.Equal(1000m, fundsAfterDelete[0].AvailableAmount);
    }

    [Fact]
    public async Task Imprint_Create_RejectsInvestmentFromDifferentClient()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        var clientAId = Guid.NewGuid();
        var clientBId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var investmentId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = clientAId,
                    TenantId = tenantId,
                    FirstName = "Ari",
                    LastName = "One",
                    Email = "ari@test.local",
                    Phone = "111",
                    HouseholdCount = 1,
                    StabilityScore = 5,
                    LifePhase = LifePhase.Stable
                },
                new Client
                {
                    Id = clientBId,
                    TenantId = tenantId,
                    FirstName = "Blake",
                    LastName = "Two",
                    Email = "blake@test.local",
                    Phone = "222",
                    HouseholdCount = 1,
                    StabilityScore = 5,
                    LifePhase = LifePhase.Stable
                });

            db.Funds.Add(new Fund
            {
                Id = fundId,
                TenantId = tenantId,
                Name = "General",
                TotalAmount = 500m,
                AvailableAmount = 500m
            });

            db.Programs.Add(new GrowIT.Core.Entities.Program
            {
                Id = programId,
                TenantId = tenantId,
                Name = "Utility Assistance",
                Description = "",
                DefaultUnitCost = 100m
            });

            db.Investments.Add(new Investment
            {
                Id = investmentId,
                TenantId = tenantId,
                ClientId = clientAId,
                FundId = fundId,
                ProgramId = programId,
                Amount = 100m,
                SnapshotUnitCost = 100m,
                PayeeName = "Power Co",
                Reason = "Utility payment",
                CreatedBy = Guid.NewGuid(),
                Status = InvestmentStatus.Approved
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);

        var response = await client.PostAsJsonAsync("/api/imprints", new CreateImprintRequest
        {
            ClientId = clientBId,
            InvestmentId = investmentId,
            Title = "Power Restored",
            Category = ImprintCategory.HousingStability,
            Outcome = ImpactOutcome.Improved,
            DateOccurred = DateTime.UtcNow,
            Notes = "Service restored"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
