using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using ClientEntity = GrowIT.Core.Entities.Client;
using ProgramEntity = GrowIT.Core.Entities.Program;

namespace GrowIT.Backend.Tests;

public class OrganizationReadinessTests
{
    [Fact]
    public async Task AscScore_ReturnsUnscored_WhenOrganizationHasTooLittleInformation()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "New Founder Org",
                CreatedAt = DateTime.UtcNow
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var response = await client.GetAsync("/api/organization-readiness/asc-score");

        response.EnsureSuccessStatusCode();
        var score = await response.ReadRequiredJsonAsync<AscScoreDto>();

        Assert.False(score.IsScored);
        Assert.Null(score.Score);
        Assert.Equal("Unscored", score.ScoreStatus);
        Assert.Contains(score.MissingItems, item => item.Contains("organization profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AscScore_ReturnsProvisional_WhenOperationalDataExistsWithoutVerificationDepth()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var programId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(CompleteTenant(tenantId, DateTime.UtcNow.AddMonths(-6)));
            db.Users.AddRange(
                NewUser(tenantId, "one@test.local"),
                NewUser(tenantId, "two@test.local"));
            db.Clients.Add(new ClientEntity
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Jordan",
                LastName = "Ready",
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Stable
            });
            db.Funds.Add(new Fund
            {
                Id = fundId,
                TenantId = tenantId,
                Name = "Community Donors",
                TotalAmount = 25000m,
                AvailableAmount = 18000m
            });
            db.Programs.Add(new ProgramEntity
            {
                Id = programId,
                TenantId = tenantId,
                Name = "Child Support",
                Description = "Support for children and households.",
                DefaultUnitCost = 300m,
                CapacityLimit = 50,
                CapacityPeriod = "Monthly"
            });
            db.Investments.AddRange(
                NewInvestment(tenantId, clientId, fundId, programId, InvestmentStatus.Completed, DateTime.UtcNow.AddMonths(-4)),
                NewInvestment(tenantId, clientId, fundId, programId, InvestmentStatus.Disbursed, DateTime.UtcNow.AddMonths(-3)),
                NewInvestment(tenantId, clientId, fundId, programId, InvestmentStatus.Approved, DateTime.UtcNow.AddMonths(-2)));
            db.Imprints.AddRange(
                NewImprint(tenantId, clientId, ImpactOutcome.Improved, DateTime.UtcNow.AddMonths(-3)),
                NewImprint(tenantId, clientId, ImpactOutcome.Maintained, DateTime.UtcNow.AddMonths(-2)));

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var response = await client.GetAsync("/api/organization-readiness/asc-score");

        response.EnsureSuccessStatusCode();
        var score = await response.ReadRequiredJsonAsync<AscScoreDto>();

        Assert.True(score.IsScored);
        Assert.Equal("Provisional", score.ScoreStatus);
        Assert.NotNull(score.Score);
        Assert.True(score.Score < 9.0m);
        Assert.Contains(score.MissingItems, item => item.Contains("documents", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(score.MissingItems, item => item.Contains("Generate a report", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AscScore_ReturnsVerified_WhenDataIsSupportedByDocumentsReportsAndImpact()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var secondFundId = Guid.NewGuid();
        var programId = Guid.NewGuid();
        var secondProgramId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(CompleteTenant(tenantId, DateTime.UtcNow.AddMonths(-14)));
            db.Users.AddRange(
                NewUser(tenantId, "one@test.local"),
                NewUser(tenantId, "two@test.local"),
                NewUser(tenantId, "three@test.local"));
            db.Clients.Add(new ClientEntity
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Morgan",
                LastName = "Capital",
                HouseholdCount = 2,
                StabilityScore = 8,
                LifePhase = LifePhase.Thriving
            });
            db.FamilyMembers.Add(new FamilyMember
            {
                Id = memberId,
                TenantId = tenantId,
                ClientId = clientId,
                FirstName = "Avery",
                LastName = "Capital",
                Relationship = "Child"
            });
            db.Funds.AddRange(
                new Fund { Id = fundId, TenantId = tenantId, Name = "General Fund", TotalAmount = 90000m, AvailableAmount = 50000m },
                new Fund { Id = secondFundId, TenantId = tenantId, Name = "Foundation Grant", TotalAmount = 60000m, AvailableAmount = 42000m });
            db.Programs.AddRange(
                new ProgramEntity { Id = programId, TenantId = tenantId, Name = "Family Stability", Description = "Stability services.", DefaultUnitCost = 500m, CapacityLimit = 80, CapacityPeriod = "Monthly" },
                new ProgramEntity { Id = secondProgramId, TenantId = tenantId, Name = "Youth Support", Description = "Youth services.", DefaultUnitCost = 250m, CapacityLimit = 120, CapacityPeriod = "Monthly" });
            db.Documents.AddRange(
                NewDocument(tenantId, clientId, DocumentCategory.ID),
                NewDocument(tenantId, clientId, DocumentCategory.Bill),
                NewDocument(tenantId, clientId, DocumentCategory.Contract),
                NewDocument(tenantId, clientId, DocumentCategory.Other),
                NewDocument(tenantId, clientId, DocumentCategory.Other));
            db.Investments.AddRange(
                NewInvestment(tenantId, clientId, fundId, programId, InvestmentStatus.Completed, DateTime.UtcNow.AddMonths(-12), memberId),
                NewInvestment(tenantId, clientId, fundId, programId, InvestmentStatus.Disbursed, DateTime.UtcNow.AddMonths(-9), memberId),
                NewInvestment(tenantId, clientId, secondFundId, secondProgramId, InvestmentStatus.Approved, DateTime.UtcNow.AddMonths(-6), memberId),
                NewInvestment(tenantId, clientId, secondFundId, secondProgramId, InvestmentStatus.Completed, DateTime.UtcNow.AddMonths(-3), memberId),
                NewInvestment(tenantId, clientId, secondFundId, secondProgramId, InvestmentStatus.Completed, DateTime.UtcNow.AddMonths(-1), memberId));
            db.Imprints.AddRange(
                NewImprint(tenantId, clientId, ImpactOutcome.Improved, DateTime.UtcNow.AddMonths(-12)),
                NewImprint(tenantId, clientId, ImpactOutcome.Maintained, DateTime.UtcNow.AddMonths(-9)),
                NewImprint(tenantId, clientId, ImpactOutcome.Improved, DateTime.UtcNow.AddMonths(-6)),
                NewImprint(tenantId, clientId, ImpactOutcome.Maintained, DateTime.UtcNow.AddMonths(-3)),
                NewImprint(tenantId, clientId, ImpactOutcome.Improved, DateTime.UtcNow.AddMonths(-1)));
            db.GrowthPlans.AddRange(
                NewGrowthPlan(tenantId, clientId, DateTime.UtcNow.AddMonths(-8)),
                NewGrowthPlan(tenantId, clientId, DateTime.UtcNow.AddMonths(-5)));
            db.Tasks.AddRange(
                NewTask(tenantId, clientId, GrowIT.Shared.Enums.TaskStatus.Completed, DateTime.UtcNow.AddMonths(-6)),
                NewTask(tenantId, clientId, GrowIT.Shared.Enums.TaskStatus.Completed, DateTime.UtcNow.AddMonths(-4)),
                NewTask(tenantId, clientId, GrowIT.Shared.Enums.TaskStatus.Pending, DateTime.UtcNow.AddMonths(-1)));
            db.ReportRuns.Add(new ReportRun
            {
                TenantId = tenantId,
                Name = "Impact Summary",
                ReportType = ReportContract.ImpactSummary,
                Format = "pdf",
                Status = "Generated",
                GeneratedAt = DateTime.UtcNow.AddMonths(-2),
                CompletedAt = DateTime.UtcNow.AddMonths(-2),
                LastDownloadedAt = DateTime.UtcNow.AddMonths(-1)
            });
            db.ReportSchedules.Add(new ReportSchedule
            {
                TenantId = tenantId,
                Name = "Monthly Impact Report",
                ReportType = ReportContract.ImpactSummary,
                Format = "pdf",
                Frequency = "Monthly",
                NextRun = DateTime.UtcNow.AddMonths(1),
                IsActive = true
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var response = await client.GetAsync("/api/organization-readiness/asc-score");

        response.EnsureSuccessStatusCode();
        var score = await response.ReadRequiredJsonAsync<AscScoreDto>();

        Assert.True(score.IsScored);
        Assert.Equal("Verified", score.ScoreStatus);
        Assert.NotNull(score.Score);
        Assert.True(score.Score >= 7.5m);
        Assert.Contains(score.Pillars, pillar => pillar.Label == "Impact & Outcome Evidence" && pillar.Status == "Strong");
    }

    private static Tenant CompleteTenant(Guid tenantId, DateTime createdAt) => new()
    {
        Id = tenantId,
        Name = "Verified Community Org",
        Address = "123 Mission Road",
        ContactEmail = "hello@verified.local",
        OrganizationType = "Community Services",
        OrganizationSize = "1-10",
        TrackPeople = true,
        TrackInvestments = true,
        TrackOutcomes = true,
        TrackPrograms = true,
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
    };

    private static User NewUser(Guid tenantId, string email) => new()
    {
        TenantId = tenantId,
        FirstName = "Test",
        LastName = "User",
        Email = email,
        UserName = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
        IsActive = true,
        EmailConfirmed = true
    };

    private static Investment NewInvestment(
        Guid tenantId,
        Guid clientId,
        Guid fundId,
        Guid programId,
        InvestmentStatus status,
        DateTime createdAt,
        Guid? memberId = null) => new()
    {
        TenantId = tenantId,
        ClientId = clientId,
        FundId = fundId,
        ProgramId = programId,
        FamilyMemberId = memberId,
        Amount = 300m,
        SnapshotUnitCost = 300m,
        Reason = "Readiness support",
        Status = status,
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
    };

    private static Imprint NewImprint(Guid tenantId, Guid clientId, ImpactOutcome outcome, DateTime dateOccurred) => new()
    {
        TenantId = tenantId,
        ClientId = clientId,
        Title = "Outcome recorded",
        Category = ImprintCategory.FinancialStability,
        Outcome = outcome,
        DateOccurred = DateTime.SpecifyKind(dateOccurred, DateTimeKind.Utc)
    };

    private static Document NewDocument(Guid tenantId, Guid clientId, DocumentCategory category) => new()
    {
        TenantId = tenantId,
        ClientId = clientId,
        FileUrl = "/uploads/test.pdf",
        FileType = "PDF",
        Category = category
    };

    private static GrowthPlan NewGrowthPlan(Guid tenantId, Guid clientId, DateTime createdAt) => new()
    {
        TenantId = tenantId,
        ClientId = clientId,
        Title = "Growth plan",
        Status = GrowthPlanStatus.Active,
        Season = Season.Growing,
        TotalGoals = 4,
        CompletedGoals = 2,
        StartDate = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
    };

    private static AppTask NewTask(Guid tenantId, Guid clientId, GrowIT.Shared.Enums.TaskStatus status, DateTime createdAt) => new()
    {
        TenantId = tenantId,
        ClientId = clientId,
        AssignedTo = Guid.NewGuid(),
        DueDate = DateTime.SpecifyKind(createdAt.AddDays(7), DateTimeKind.Utc),
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
        Status = status,
        Notes = "Readiness follow-up"
    };
}
