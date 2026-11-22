using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ClientEntity = GrowIT.Core.Entities.Client;
using TaskStatus = GrowIT.Shared.Enums.TaskStatus;

namespace GrowIT.Backend.Tests;

/// <summary>
/// Service records, outcomes and growth plans fixes from the Nov 15 browser pass:
/// service record details/edit (#38), outcomes not auto-verified + edit/delete (#39/#41),
/// outcome follow-ups appearing in Follow-ups (#42) and growth plan goals/owner/progress (#44).
/// </summary>
public class RecordsAuditFixTests
{
    // ---------------------------------------------------------------- Service records (#38)

    [Fact]
    public async Task ServiceRecordDetail_IncludesMoneySourceAndWhoWasPaid()
    {
        using var seed = await SeedOrgAsync();
        var investmentId = await SeedInvestmentAsync(seed, InvestmentStatus.Pending, 120m, payee: "City Power");
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        var detail = await client.GetFromJsonAsync<InvestmentDetailDto>($"/api/investments/{investmentId}");

        Assert.NotNull(detail);
        Assert.Equal("General Relief", detail!.FundName);
        Assert.Equal(seed.FundId, detail.FundId);
        Assert.Equal("City Power", detail.PayeeName);
        Assert.Equal("Rent Assistance", detail.Category);
        Assert.Equal(seed.ClientId, detail.ClientId);
        Assert.False(detail.MoneyLocked);
    }

    [Fact]
    public async Task ServiceRecordUpdate_PendingRecordCanChangeCostAndMoneySource()
    {
        using var seed = await SeedOrgAsync();
        var investmentId = await SeedInvestmentAsync(seed, InvestmentStatus.Pending, 120m);
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        var response = await client.PutAsJsonAsync($"/api/investments/{investmentId}", new UpdateInvestmentRequest
        {
            FundId = seed.SecondFundId,
            ProgramId = seed.ProgramId,
            Amount = 150m,
            PayeeName = "  Landlord LLC ",
            Reason = "October rent, corrected"
        });

        response.EnsureSuccessStatusCode();
        var detail = await response.ReadRequiredJsonAsync<InvestmentDetailDto>();
        Assert.Equal(150m, detail.Amount);
        Assert.Equal(seed.SecondFundId, detail.FundId);
        Assert.Equal("Emergency Fund", detail.FundName);
        Assert.Equal("Landlord LLC", detail.PayeeName);
        Assert.Equal("October rent, corrected", detail.Purpose);

        // Pending records don't reserve money, so no money source balance moved.
        await using var scope = seed.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var funds = await db.Funds.IgnoreQueryFilters().Where(f => f.TenantId == seed.TenantId).ToListAsync();
        Assert.All(funds, f => Assert.Equal(f.TotalAmount, f.AvailableAmount));
    }

    [Fact]
    public async Task ServiceRecordUpdate_ApprovedRecordKeepsCostButAllowsDescription()
    {
        using var seed = await SeedOrgAsync();
        var investmentId = await SeedInvestmentAsync(seed, InvestmentStatus.Approved, 120m);
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        var costChange = await client.PutAsJsonAsync($"/api/investments/{investmentId}", new UpdateInvestmentRequest
        {
            FundId = seed.FundId,
            ProgramId = seed.ProgramId,
            Amount = 999m,
            Reason = "Rent"
        });
        Assert.Equal(HttpStatusCode.BadRequest, costChange.StatusCode);
        Assert.Contains("approved", await costChange.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var descriptionChange = await client.PutAsJsonAsync($"/api/investments/{investmentId}", new UpdateInvestmentRequest
        {
            FundId = seed.FundId,
            ProgramId = seed.ProgramId,
            Amount = 120m,
            PayeeName = "Landlord LLC",
            Reason = "Rent for October"
        });
        descriptionChange.EnsureSuccessStatusCode();
        var detail = await descriptionChange.ReadRequiredJsonAsync<InvestmentDetailDto>();
        Assert.Equal(120m, detail.Amount);
        Assert.Equal("Rent for October", detail.Purpose);
        Assert.Equal("Landlord LLC", detail.PayeeName);
        Assert.True(detail.MoneyLocked);
    }

    [Fact]
    public async Task ServiceRecordUpdate_PaidRecordCostIsFinal()
    {
        using var seed = await SeedOrgAsync();
        var investmentId = await SeedInvestmentAsync(seed, InvestmentStatus.Disbursed, 120m);
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Admin");

        var response = await client.PutAsJsonAsync($"/api/investments/{investmentId}", new UpdateInvestmentRequest
        {
            FundId = seed.SecondFundId,
            ProgramId = seed.ProgramId,
            Amount = 120m,
            Reason = "Rent"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paid", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServiceRecordUpdate_RejectsMemberRoleAndOtherOrganizationsRecords()
    {
        using var seed = await SeedOrgAsync();
        var investmentId = await SeedInvestmentAsync(seed, InvestmentStatus.Pending, 120m);
        var body = new UpdateInvestmentRequest { FundId = seed.FundId, ProgramId = seed.ProgramId, Amount = 120m, Reason = "Rent" };

        using var member = seed.Factory.CreateTenantClient(seed.TenantId, role: "Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/investments/{investmentId}", body)).StatusCode);

        using var otherOrg = seed.Factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await otherOrg.PutAsJsonAsync($"/api/investments/{investmentId}", body)).StatusCode);
    }

    // ---------------------------------------------------------------- Outcomes (#39, #41, #42)

    [Fact]
    public async Task NewOutcome_IsNotLabelledVerified()
    {
        using var seed = await SeedOrgAsync();
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        (await client.PostAsJsonAsync("/api/imprints", NewOutcome(seed, "Kept housing", followup: null))).EnsureSuccessStatusCode();

        var list = await client.GetFromJsonAsync<List<ImprintListDto>>("/api/imprints");
        var item = Assert.Single(list ?? []);
        Assert.False(item.IsVerified);
        Assert.Equal(ImpactOutcome.Improved, item.Outcome);
    }

    [Fact]
    public async Task OutcomeWithFollowupDate_CreatesOneFollowUpThatEditsUpdateInsteadOfDuplicating()
    {
        using var seed = await SeedOrgAsync();
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");
        var followup = new DateTime(2026, 12, 1);

        var created = await client.PostAsJsonAsync("/api/imprints", NewOutcome(seed, "Kept housing", followup));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var outcome = await created.ReadRequiredJsonAsync<ImprintResponseDto>();

        var task = Assert.Single(await PendingTasksAsync(client));
        Assert.Equal(seed.ClientId, task.ClientId);
        Assert.Equal(ActionItemType.ClientFollowUp, task.Type);
        Assert.Equal("Follow up: Kept housing", task.Notes);
        Assert.Equal(followup.Date, task.DueDate.Date);
        Assert.Equal(seed.UserId, task.AssignedTo);

        // Edit title only: same follow-up, new wording, still one item.
        (await client.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "Kept housing 90 days", followup))).EnsureSuccessStatusCode();
        task = Assert.Single(await PendingTasksAsync(client));
        Assert.Equal("Follow up: Kept housing 90 days", task.Notes);

        // Move the date: the same follow-up moves.
        var moved = new DateTime(2026, 12, 15);
        (await client.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "Kept housing 90 days", moved))).EnsureSuccessStatusCode();
        task = Assert.Single(await PendingTasksAsync(client));
        Assert.Equal(moved.Date, task.DueDate.Date);

        // Clear the date: the open follow-up goes away.
        (await client.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "Kept housing 90 days", null))).EnsureSuccessStatusCode();
        Assert.Empty(await PendingTasksAsync(client));

        var detail = await client.GetFromJsonAsync<ImprintResponseDto>($"/api/imprints/{outcome.Id}");
        Assert.Null(detail!.FollowupDate);
        Assert.Equal("Kept housing 90 days", detail.Title);
    }

    [Fact]
    public async Task OutcomeFollowUp_CompletedThenNewDate_ReopensSameFollowUp()
    {
        using var seed = await SeedOrgAsync();
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        var outcome = await (await client.PostAsJsonAsync("/api/imprints", NewOutcome(seed, "Started job", new DateTime(2026, 12, 1))))
            .ReadRequiredJsonAsync<ImprintResponseDto>();
        var task = Assert.Single(await PendingTasksAsync(client));
        (await client.PatchAsJsonAsync($"/api/tasks/{task.Id}/status", new UpdateTaskStatusRequest { Status = TaskStatus.Completed })).EnsureSuccessStatusCode();

        // Unrelated edit keeps the completed follow-up completed.
        (await client.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "Started full-time job", new DateTime(2026, 12, 1)))).EnsureSuccessStatusCode();
        Assert.Empty(await PendingTasksAsync(client));

        // A new follow-up date puts it back on the list (still one item overall).
        (await client.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "Started full-time job", new DateTime(2027, 1, 5)))).EnsureSuccessStatusCode();
        var reopened = Assert.Single(await PendingTasksAsync(client));
        Assert.Equal(task.Id, reopened.Id);
        Assert.Single(await AllTasksAsync(client));
    }

    [Fact]
    public async Task DeleteOutcome_RemovesItAndItsOpenFollowUp()
    {
        using var seed = await SeedOrgAsync();
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");
        var outcome = await (await client.PostAsJsonAsync("/api/imprints", NewOutcome(seed, "Kept housing", new DateTime(2026, 12, 1))))
            .ReadRequiredJsonAsync<ImprintResponseDto>();

        var response = await client.DeleteAsync($"/api/imprints/{outcome.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/imprints/{outcome.Id}")).StatusCode);
        Assert.Empty(await AllTasksAsync(client));
    }

    [Fact]
    public async Task OutcomeEditAndDelete_EnforceRolesAndOrganization()
    {
        using var seed = await SeedOrgAsync();
        using var writer = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");
        var outcome = await (await writer.PostAsJsonAsync("/api/imprints", NewOutcome(seed, "Kept housing", null)))
            .ReadRequiredJsonAsync<ImprintResponseDto>();

        using var member = seed.Factory.CreateTenantClient(seed.TenantId, role: "Member");
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "x", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.DeleteAsync($"/api/imprints/{outcome.Id}")).StatusCode);

        using var otherOrg = seed.Factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await otherOrg.PutAsJsonAsync($"/api/imprints/{outcome.Id}", EditOf(outcome, "x", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOrg.DeleteAsync($"/api/imprints/{outcome.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOrg.GetAsync($"/api/imprints/{outcome.Id}")).StatusCode);
    }

    // ---------------------------------------------------------------- Growth plans (#44)

    [Fact]
    public async Task GrowthPlan_CreateWithOwnerAndGoals_ThenRecordProgress()
    {
        using var seed = await SeedOrgAsync();
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");

        var created = await client.PostAsJsonAsync("/api/growthplans", new CreateGrowthPlanRequest
        {
            PersonId = seed.ClientId,
            Title = "Stable housing",
            StartDate = new DateTime(2026, 11, 1),
            AssignedToUserId = seed.UserId,
            TotalGoals = 4
        });
        created.EnsureSuccessStatusCode();
        var plan = await created.ReadRequiredJsonAsync<GrowthPlanListDto>();
        Assert.Equal(4, plan.TotalGoals);
        Assert.Equal(0, plan.CompletedGoals);
        Assert.Equal(seed.UserId, plan.AssignedToUserId);
        Assert.Equal("Casey Worker", plan.AssignedToUserName);

        var progress = await client.PatchAsJsonAsync($"/api/growthplans/{plan.Id}/progress", new UpdateGrowthPlanProgressRequest { CompletedGoals = 3 });
        progress.EnsureSuccessStatusCode();
        plan = await progress.ReadRequiredJsonAsync<GrowthPlanListDto>();
        Assert.Equal(3, plan.CompletedGoals);
        Assert.Equal(75m, plan.ProgressPercentage);

        var tooMany = await client.PatchAsJsonAsync($"/api/growthplans/{plan.Id}/progress", new UpdateGrowthPlanProgressRequest { CompletedGoals = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task GrowthPlan_UpdateSetsGoalsAndOwner_AndRejectsOwnerFromAnotherOrganization()
    {
        using var seed = await SeedOrgAsync();
        var outsiderId = Guid.NewGuid();
        await seed.Factory.SeedAsync(db =>
        {
            var otherTenant = Guid.NewGuid();
            db.Tenants.Add(new Tenant { Id = otherTenant, Name = "Other Org", ContactEmail = "other@example.test" });
            db.Users.Add(NewUser(otherTenant, outsiderId, "Out", "Sider", "outsider@example.test"));
            return Task.CompletedTask;
        });
        using var client = seed.Factory.CreateTenantClient(seed.TenantId, seed.UserId, role: "Case Manager");
        var plan = await (await client.PostAsJsonAsync("/api/growthplans", new CreateGrowthPlanRequest
        {
            PersonId = seed.ClientId,
            Title = "Income plan",
            StartDate = new DateTime(2026, 11, 1)
        })).ReadRequiredJsonAsync<GrowthPlanListDto>();
        Assert.Null(plan.AssignedToUserName);

        var foreignOwner = await client.PostAsJsonAsync("/api/growthplans", new CreateGrowthPlanRequest
        {
            PersonId = seed.ClientId,
            Title = "Bad owner",
            AssignedToUserId = outsiderId
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignOwner.StatusCode);

        var updated = await client.PutAsJsonAsync($"/api/growthplans/{plan.Id}", new UpdateGrowthPlanRequest
        {
            Title = "Income plan",
            Season = Season.Growing,
            Status = GrowthPlanStatus.OnHold,
            AssignedToUserId = seed.UserId,
            TotalGoals = 5,
            CompletedGoals = 2
        });
        updated.EnsureSuccessStatusCode();
        plan = await updated.ReadRequiredJsonAsync<GrowthPlanListDto>();
        Assert.Equal(5, plan.TotalGoals);
        Assert.Equal(2, plan.CompletedGoals);
        Assert.Equal(40m, plan.ProgressPercentage);
        Assert.Equal("Casey Worker", plan.AssignedToUserName);

        var foreignUpdate = await client.PutAsJsonAsync($"/api/growthplans/{plan.Id}", new UpdateGrowthPlanRequest
        {
            Title = "Income plan",
            Status = GrowthPlanStatus.Active,
            AssignedToUserId = outsiderId
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreignUpdate.StatusCode);

        var targetBeforeStart = await client.PutAsJsonAsync($"/api/growthplans/{plan.Id}", new UpdateGrowthPlanRequest
        {
            Title = "Income plan",
            Status = GrowthPlanStatus.Active,
            AssignedToUserId = seed.UserId,
            TargetEndDate = new DateTime(2026, 10, 1)
        });
        Assert.Equal(HttpStatusCode.BadRequest, targetBeforeStart.StatusCode);

        using var member = seed.Factory.CreateTenantClient(seed.TenantId, role: "Member");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PatchAsJsonAsync($"/api/growthplans/{plan.Id}/progress", new UpdateGrowthPlanProgressRequest { CompletedGoals = 1 })).StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record OrgSeed(
        GrowItApiFactory Factory, Guid TenantId, Guid UserId, Guid ClientId, Guid FundId, Guid SecondFundId, Guid ProgramId)
        : IDisposable
    {
        public void Dispose() => Factory.Dispose();
    }

    private static async Task<OrgSeed> SeedOrgAsync()
    {
        var factory = new GrowItApiFactory();
        var seed = new OrgSeed(factory, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(new Tenant { Id = seed.TenantId, Name = "Records Test Org", ContactEmail = "org@example.test" });
            db.Users.Add(NewUser(seed.TenantId, seed.UserId, "Casey", "Worker", "casey@example.test"));
            db.Clients.Add(new ClientEntity
            {
                Id = seed.ClientId,
                TenantId = seed.TenantId,
                FirstName = "Maria",
                LastName = "Alvarez",
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Crisis
            });
            db.Funds.AddRange(
                new Fund { Id = seed.FundId, TenantId = seed.TenantId, Name = "General Relief", TotalAmount = 1000m, AvailableAmount = 1000m },
                new Fund { Id = seed.SecondFundId, TenantId = seed.TenantId, Name = "Emergency Fund", TotalAmount = 500m, AvailableAmount = 500m });
            db.Programs.Add(new GrowIT.Core.Entities.Program
            {
                Id = seed.ProgramId,
                TenantId = seed.TenantId,
                Name = "Rent Assistance",
                DefaultUnitCost = 100m
            });
            return Task.CompletedTask;
        });

        return seed;
    }

    private static async Task<Guid> SeedInvestmentAsync(OrgSeed seed, InvestmentStatus status, decimal amount, string payee = "")
    {
        var id = Guid.NewGuid();
        await seed.Factory.SeedAsync(db =>
        {
            db.Investments.Add(new Investment
            {
                Id = id,
                TenantId = seed.TenantId,
                ClientId = seed.ClientId,
                FundId = seed.FundId,
                ProgramId = seed.ProgramId,
                Amount = amount,
                SnapshotUnitCost = 100m,
                PayeeName = payee,
                Reason = "Rent",
                Status = status
            });
            return Task.CompletedTask;
        });
        return id;
    }

    private static CreateImprintRequest NewOutcome(OrgSeed seed, string title, DateTime? followup) => new()
    {
        ClientId = seed.ClientId,
        Title = title,
        Category = ImprintCategory.HousingStability,
        Outcome = ImpactOutcome.Improved,
        DateOccurred = new DateTime(2026, 11, 10),
        Notes = "Lease renewed.",
        FollowupDate = followup
    };

    private static UpdateImprintRequest EditOf(ImprintResponseDto outcome, string title, DateTime? followup) => new()
    {
        FamilyMemberId = outcome.FamilyMemberId,
        InvestmentId = outcome.InvestmentId,
        Title = title,
        Category = outcome.Category,
        Outcome = outcome.Outcome,
        DateOccurred = outcome.DateOccurred,
        Notes = outcome.Notes,
        FollowupDate = followup
    };

    private static async Task<List<TaskListDto>> PendingTasksAsync(HttpClient client) =>
        await client.GetFromJsonAsync<List<TaskListDto>>("/api/tasks") ?? [];

    private static async Task<List<TaskListDto>> AllTasksAsync(HttpClient client) =>
        await client.GetFromJsonAsync<List<TaskListDto>>("/api/tasks?includeCompleted=true") ?? [];

    private static User NewUser(Guid tenantId, Guid userId, string firstName, string lastName, string email) => new()
    {
        Id = userId,
        TenantId = tenantId,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        UserName = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
        EmailConfirmed = true,
        IsActive = true
    };
}
