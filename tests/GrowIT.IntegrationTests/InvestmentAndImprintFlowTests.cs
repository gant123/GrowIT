using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using ClientEntity = GrowIT.Core.Entities.Client;

namespace GrowIT.Backend.Tests;

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
            db.Clients.Add(new ClientEntity
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
        Assert.Equal(1000m, fundsAfterCreate[0].AvailableAmount);

        var approveResponse = await client.PostAsJsonAsync($"/api/investments/{investmentId}/approve", new ApproveInvestmentRequest
        {
            ApprovedBy = "Integration Test"
        });
        approveResponse.EnsureSuccessStatusCode();

        var fundsAfterApproval = await (await client.GetAsync("/api/financials/funds"))
            .ReadRequiredJsonAsync<List<FundDto>>();
        Assert.Single(fundsAfterApproval);
        Assert.Equal(800m, fundsAfterApproval[0].AvailableAmount);

        var deleteResponse = await client.DeleteAsync($"/api/investments/{investmentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var fundsAfterDelete = await (await client.GetAsync("/api/financials/funds"))
            .ReadRequiredJsonAsync<List<FundDto>>();
        Assert.Single(fundsAfterDelete);
        Assert.Equal(1000m, fundsAfterDelete[0].AvailableAmount);
    }

    [Fact]
    public async Task Investment_SecondApprovalCannotOverspendFund()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var firstInvestmentId = Guid.NewGuid();
        var secondInvestmentId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.Add(new ClientEntity
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Jordan",
                LastName = "Client",
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Crisis
            });

            db.Funds.Add(new Fund
            {
                Id = fundId,
                TenantId = tenantId,
                Name = "Emergency Fund",
                TotalAmount = 100m,
                AvailableAmount = 100m
            });

            db.Programs.Add(new GrowIT.Core.Entities.Program
            {
                Id = programId,
                TenantId = tenantId,
                Name = "Emergency Assistance",
                DefaultUnitCost = 100m
            });

            db.Investments.AddRange(
                new Investment
                {
                    Id = firstInvestmentId,
                    TenantId = tenantId,
                    ClientId = clientId,
                    FundId = fundId,
                    ProgramId = programId,
                    Amount = 100m,
                    SnapshotUnitCost = 100m,
                    Reason = "First request",
                    Status = InvestmentStatus.Pending
                },
                new Investment
                {
                    Id = secondInvestmentId,
                    TenantId = tenantId,
                    ClientId = clientId,
                    FundId = fundId,
                    ProgramId = programId,
                    Amount = 100m,
                    SnapshotUnitCost = 100m,
                    Reason = "Second request",
                    Status = InvestmentStatus.Pending
                });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId, role: "Admin");
        var firstApproval = await client.PostAsJsonAsync($"/api/investments/{firstInvestmentId}/approve", new ApproveInvestmentRequest());
        firstApproval.EnsureSuccessStatusCode();

        var secondApproval = await client.PostAsJsonAsync($"/api/investments/{secondInvestmentId}/approve", new ApproveInvestmentRequest());
        Assert.Equal(HttpStatusCode.BadRequest, secondApproval.StatusCode);

        var funds = await (await client.GetAsync("/api/financials/funds"))
            .ReadRequiredJsonAsync<List<FundDto>>();

        Assert.Single(funds);
        Assert.Equal(0m, funds[0].AvailableAmount);
    }

    [Fact]
    public async Task MemberProfile_ReturnsRequestedFundedAndRemainingNeed()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var programId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.Add(new ClientEntity
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Riley",
                LastName = "Household",
                HouseholdCount = 2,
                StabilityScore = 5,
                LifePhase = LifePhase.Crisis
            });

            db.FamilyMembers.Add(new FamilyMember
            {
                Id = memberId,
                TenantId = tenantId,
                ClientId = clientId,
                FirstName = "Avery",
                LastName = "Household",
                Relationship = "Child"
            });

            db.Funds.Add(new Fund
            {
                Id = fundId,
                TenantId = tenantId,
                Name = "Child Support Fund",
                TotalAmount = 1000m,
                AvailableAmount = 800m
            });

            db.Programs.Add(new GrowIT.Core.Entities.Program
            {
                Id = programId,
                TenantId = tenantId,
                Name = "School Support",
                DefaultUnitCost = 150m
            });

            db.Investments.AddRange(
                new Investment
                {
                    TenantId = tenantId,
                    ClientId = clientId,
                    FamilyMemberId = memberId,
                    FundId = fundId,
                    ProgramId = programId,
                    Amount = 200m,
                    SnapshotUnitCost = 150m,
                    Reason = "Funded uniforms",
                    Status = InvestmentStatus.Approved,
                    CreatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Investment
                {
                    TenantId = tenantId,
                    ClientId = clientId,
                    FamilyMemberId = memberId,
                    FundId = fundId,
                    ProgramId = programId,
                    Amount = 300m,
                    SnapshotUnitCost = 150m,
                    Reason = "Pending school fees",
                    Status = InvestmentStatus.Pending,
                    CreatedAt = DateTime.UtcNow.AddDays(-1)
                });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId, role: "Admin");
        var response = await client.GetAsync($"/api/clients/members/{memberId}");

        response.EnsureSuccessStatusCode();
        var profile = await response.ReadRequiredJsonAsync<FamilyMemberProfileDto>();

        Assert.Equal(500m, profile.RequestedNeed);
        Assert.Equal(200m, profile.FundedAmount);
        Assert.Equal(300m, profile.RemainingNeed);
        Assert.NotNull(profile.LastSupportDate);
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
                new ClientEntity
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
                new ClientEntity
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
