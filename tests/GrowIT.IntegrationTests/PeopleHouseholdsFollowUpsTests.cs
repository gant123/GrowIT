using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Services;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = GrowIT.Shared.Enums.TaskStatus;

namespace GrowIT.Backend.Tests;

/// <summary>
/// People / Households / Follow-ups behavior from the Nov 15 browser pass: deleting a person
/// safely, renaming/deleting households and removing members, and a person's next follow-up
/// date showing up (once) in Follow-ups.
/// </summary>
public class PeopleHouseholdsFollowUpsTests
{
    private static readonly DateTime InTwoWeeks = DateTime.UtcNow.Date.AddDays(14);
    private static readonly DateTime InThreeWeeks = DateTime.UtcNow.Date.AddDays(21);

    // ---------------------------------------------------------------- next follow-up date

    [Fact]
    public async Task CreatePerson_WithNextFollowUpDate_CreatesOnePendingFollowUp()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);

        var tasks = await GetTasksAsync(http);
        var task = Assert.Single(tasks);
        Assert.Equal(clientId, task.ClientId);
        Assert.Equal(ActionItemType.ClientFollowUp, task.Type);
        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Equal(InTwoWeeks, task.DueDate.Date);
        Assert.Equal("Check in with Maria Alvarez", task.Notes);
        Assert.Equal(actorId, task.AssignedTo);
        Assert.Equal(actorId, task.CreatedByUserId);
        Assert.True(task.IsFromNextFollowUpDate);
    }

    [Fact]
    public async Task CreatePerson_WithoutNextFollowUpDate_CreatesNoFollowUp()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        await CreatePersonAsync(http, "Luis", "Alvarez", nextFollowUp: null);

        Assert.Empty(await GetTasksAsync(http));
    }

    [Fact]
    public async Task UpdatePerson_SameDateAgain_IsIdempotent_AndRenameUpdatesDefaultTitle()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);

        // Save twice with the same date (e.g. a double-click or an unrelated edit) and a rename.
        var update = PersonRequest("Maria", "Alvarez-Ruiz", InTwoWeeks);
        (await http.PutAsJsonAsync($"/api/clients/{clientId}", update)).EnsureSuccessStatusCode();
        (await http.PutAsJsonAsync($"/api/clients/{clientId}", update)).EnsureSuccessStatusCode();

        var task = Assert.Single(await GetTasksAsync(http));
        Assert.Equal("Check in with Maria Alvarez-Ruiz", task.Notes);
        Assert.Equal(InTwoWeeks, task.DueDate.Date);
    }

    [Fact]
    public async Task UpdatePerson_ChangedDate_MovesThePendingFollowUp_KeepingEditedTextAndAssignee()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var otherUserId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Users.Add(NewUser(tenantId, otherUserId, "Avery", "Caseworker", "avery@example.test"));
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);

        // Someone reassigns the follow-up and rewrites its text in Follow-ups.
        var original = Assert.Single(await GetTasksAsync(http));
        (await http.PutAsJsonAsync($"/api/tasks/{original.Id}", new UpdateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = otherUserId,
            Type = ActionItemType.ClientFollowUp,
            DueDate = original.DueDate,
            Notes = "Check rent arrears with Maria",
            Status = TaskStatus.Pending
        })).EnsureSuccessStatusCode();

        (await http.PutAsJsonAsync($"/api/clients/{clientId}", PersonRequest("Maria", "Alvarez", InThreeWeeks)))
            .EnsureSuccessStatusCode();

        var moved = Assert.Single(await GetTasksAsync(http));
        Assert.Equal(InThreeWeeks, moved.DueDate.Date);
        Assert.Equal(TaskStatus.Pending, moved.Status);
        Assert.Equal(otherUserId, moved.AssignedTo);
        Assert.Equal("Check rent arrears with Maria", moved.Notes);
        Assert.True(moved.IsFromNextFollowUpDate);
    }

    [Fact]
    public async Task UpdatePerson_ClearedDate_RemovesOnlyTheAutoCreatedPendingFollowUp()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);

        // A follow-up added by hand for the same person and the same day must survive.
        (await http.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = actorId,
            Type = ActionItemType.Documentation,
            DueDate = InTwoWeeks,
            Notes = "Collect the signed lease"
        })).EnsureSuccessStatusCode();

        (await http.PutAsJsonAsync($"/api/clients/{clientId}", PersonRequest("Maria", "Alvarez", null)))
            .EnsureSuccessStatusCode();

        var remaining = Assert.Single(await GetTasksAsync(http));
        Assert.Equal("Collect the signed lease", remaining.Notes);
        Assert.False(remaining.IsFromNextFollowUpDate);
    }

    [Fact]
    public async Task UpdatePerson_NewDateAfterCompletedCheckIn_KeepsHistoryAndAddsANewOne()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);

        var first = Assert.Single(await GetTasksAsync(http));
        (await http.PatchAsJsonAsync($"/api/tasks/{first.Id}/status", new UpdateTaskStatusRequest { Status = TaskStatus.Completed }))
            .EnsureSuccessStatusCode();

        (await http.PutAsJsonAsync($"/api/clients/{clientId}", PersonRequest("Maria", "Alvarez", InThreeWeeks)))
            .EnsureSuccessStatusCode();

        var tasks = await GetTasksAsync(http);
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, t => t.Id == first.Id && t.Status == TaskStatus.Completed);
        Assert.Contains(tasks, t => t.Status == TaskStatus.Pending && t.DueDate.Date == InThreeWeeks);
    }

    [Fact]
    public async Task UpdatePerson_DateSavedBeforeTheSyncExisted_GetsItsFollowUpOnNextSave()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var clientId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            var legacy = NewPerson(tenantId, clientId, "Maria", "Alvarez");
            legacy.NextFollowupDate = InTwoWeeks;
            db.Clients.Add(legacy);
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        Assert.Empty(await GetTasksAsync(http));

        (await http.PutAsJsonAsync($"/api/clients/{clientId}", PersonRequest("Maria", "Alvarez", InTwoWeeks)))
            .EnsureSuccessStatusCode();

        var task = Assert.Single(await GetTasksAsync(http));
        Assert.Equal(InTwoWeeks, task.DueDate.Date);
        Assert.True(task.IsFromNextFollowUpDate);
    }

    [Fact]
    public async Task DeletingTheProfileFollowUp_ClearsTheDate_AndALaterSaveDoesNotBringItBack()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);
        var task = Assert.Single(await GetTasksAsync(http));

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);

        var detail = await http.GetFromJsonAsync<ClientDetailDto>($"/api/clients/{clientId}");
        Assert.Null(detail!.NextFollowupDate);

        // The edit form now carries no date; saving the profile must not recreate the follow-up.
        (await http.PutAsJsonAsync($"/api/clients/{clientId}", PersonRequest("Maria", "Alvarez", detail.NextFollowupDate)))
            .EnsureSuccessStatusCode();
        Assert.Empty(await GetTasksAsync(http));
    }

    [Fact]
    public async Task DeletingAHandMadeFollowUp_LeavesTheProfileDateAlone()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", InTwoWeeks);
        var create = await http.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = actorId,
            DueDate = InTwoWeeks,
            Notes = "Drop off school forms"
        });
        var manualId = await create.ReadGuidPropertyAsync("taskId");

        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/tasks/{manualId}")).StatusCode);

        var detail = await http.GetFromJsonAsync<ClientDetailDto>($"/api/clients/{clientId}");
        Assert.Equal(InTwoWeeks, detail!.NextFollowupDate!.Value.Date);
        Assert.Single(await GetTasksAsync(http));
    }

    [Fact]
    public void FollowUpKey_IsDeterministicPerTenantPersonAndDate()
    {
        var tenant = Guid.NewGuid();
        var person = Guid.NewGuid();

        Assert.Equal(
            PersonFollowUps.KeyFor(tenant, person, InTwoWeeks),
            PersonFollowUps.KeyFor(tenant, person, InTwoWeeks.AddHours(5)));
        Assert.NotEqual(
            PersonFollowUps.KeyFor(tenant, person, InTwoWeeks),
            PersonFollowUps.KeyFor(tenant, person, InThreeWeeks));
        Assert.NotEqual(
            PersonFollowUps.KeyFor(tenant, person, InTwoWeeks),
            PersonFollowUps.KeyFor(Guid.NewGuid(), person, InTwoWeeks));
    }

    [Fact]
    public async Task EditFollowUp_Put_UpdatesFieldsAndReturnsDto()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");
        var clientId = await CreatePersonAsync(http, "Maria", "Alvarez", null);
        var create = await http.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = actorId,
            DueDate = InTwoWeeks,
            Notes = "Call about bus pass"
        });
        create.EnsureSuccessStatusCode();
        var taskId = await create.ReadGuidPropertyAsync("taskId");

        var response = await http.PutAsJsonAsync($"/api/tasks/{taskId}", new UpdateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = actorId,
            Type = ActionItemType.Funding,
            DueDate = InThreeWeeks,
            Notes = "Apply for the bus pass grant",
            Status = TaskStatus.Pending
        });

        response.EnsureSuccessStatusCode();
        var dto = await response.ReadRequiredJsonAsync<TaskListDto>();
        Assert.Equal("Apply for the bus pass grant", dto.Notes);
        Assert.Equal(ActionItemType.Funding, dto.Type);
        Assert.Equal(InThreeWeeks, dto.DueDate.Date);
        Assert.NotNull(dto.UpdatedAt);
    }

    [Fact]
    public async Task CreateFollowUp_WithoutPerson_ReturnsFriendlyValidation()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ClientId = Guid.Empty,
            AssignedTo = actorId,
            DueDate = InTwoWeeks,
            Notes = "Something"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Choose the person", await response.Content.ReadAsStringAsync());
    }

    // ---------------------------------------------------------------- delete person

    [Fact]
    public async Task DeletePerson_WithServiceRecordsOrOutcomes_Returns409SayingWhatIsLinked()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var clientId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(NewPerson(tenantId, clientId, "Maria", "Alvarez"));
            db.Investments.Add(new Investment { TenantId = tenantId, ClientId = clientId, Amount = 125m, Reason = "Rent" });
            db.Investments.Add(new Investment { TenantId = tenantId, ClientId = clientId, Amount = 40m, Reason = "Bus pass" });
            db.Imprints.Add(new Imprint { TenantId = tenantId, ClientId = clientId, Title = "Kept housing" });
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/clients/{clientId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.ReadRequiredJsonAsync<ClientDeleteBlockedDto>();
        Assert.Equal(2, body.ServiceRecords);
        Assert.Equal(1, body.Outcomes);
        Assert.Contains("2 service records and 1 outcome", body.Message);
        Assert.Contains("Maria Alvarez", body.Message);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id == clientId));
    }

    [Fact]
    public async Task DeletePerson_WithGrowthPlanOrDocument_Returns409()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var clientId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(NewPerson(tenantId, clientId, "Maria", "Alvarez"));
            db.GrowthPlans.Add(new GrowthPlan { TenantId = tenantId, ClientId = clientId, Title = "Stable housing" });
            db.Documents.Add(new Document { TenantId = tenantId, ClientId = clientId, FileUrl = "lease.pdf", FileType = "PDF" });
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/clients/{clientId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.ReadRequiredJsonAsync<ClientDeleteBlockedDto>();
        Assert.Equal(1, body.GrowthPlans);
        Assert.Equal(1, body.Documents);
        Assert.Contains("1 growth plan and 1 document", body.Message);
    }

    [Fact]
    public async Task DeletePerson_WithoutLinkedRecords_RemovesFollowUpsFamilyAndHouseholdHead()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var clientId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Households.Add(new Household { Id = householdId, TenantId = tenantId, Name = "Alvarez Household", PrimaryClientId = clientId });
            var maria = NewPerson(tenantId, clientId, "Maria", "Alvarez");
            maria.HouseholdId = householdId;
            maria.HouseholdRole = HouseholdRole.Head;
            db.Clients.Add(maria);
            var luis = NewPerson(tenantId, otherId, "Luis", "Alvarez");
            luis.HouseholdId = householdId;
            luis.HouseholdRole = HouseholdRole.Spouse;
            db.Clients.Add(luis);
            db.FamilyMembers.Add(new FamilyMember { TenantId = tenantId, ClientId = clientId, FirstName = "Sofia", LastName = "Alvarez" });
            db.Tasks.Add(new AppTask { TenantId = tenantId, ClientId = clientId, AssignedTo = actorId, Notes = "Call Maria", DueDate = InTwoWeeks });
            db.Tasks.Add(new AppTask { TenantId = tenantId, ClientId = otherId, AssignedTo = actorId, Notes = "Call Luis", DueDate = InTwoWeeks });
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/clients/{clientId}");

        response.EnsureSuccessStatusCode();
        var result = await response.ReadRequiredJsonAsync<ClientDeleteResultDto>();
        Assert.Equal("Deleted Maria Alvarez.", result.Message);
        Assert.Equal(1, result.FollowUpsRemoved);
        Assert.Equal(1, result.FamilyMembersRemoved);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id == clientId));
        Assert.False(await db.FamilyMembers.IgnoreQueryFilters().AnyAsync(f => f.ClientId == clientId));
        var remainingTask = await db.Tasks.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(otherId, remainingTask.ClientId);
        var household = await db.Households.IgnoreQueryFilters().SingleAsync(h => h.Id == householdId);
        Assert.Null(household.PrimaryClientId);
        var luisAfter = await db.Clients.IgnoreQueryFilters().SingleAsync(c => c.Id == otherId);
        Assert.Equal(householdId, luisAfter.HouseholdId);
    }

    [Fact]
    public async Task DeletePerson_FromAnotherTenant_Returns404AndKeepsThem()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var otherTenant = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(NewPerson(otherTenant, clientId, "Other", "Tenant"));
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Admin");

        var response = await http.DeleteAsync($"/api/clients/{clientId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.Id == clientId));
    }

    [Theory]
    [InlineData("Analyst")]
    [InlineData("Member")]
    public async Task NewDeleteAndRenameEndpoints_AreForbiddenForReadOnlyRoles(string role)
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: role);

        var responses = new[]
        {
            await http.DeleteAsync($"/api/clients/{Guid.NewGuid()}"),
            await http.DeleteAsync($"/api/households/{Guid.NewGuid()}"),
            await http.DeleteAsync($"/api/households/{Guid.NewGuid()}/members/{Guid.NewGuid()}"),
            await http.PutAsJsonAsync($"/api/households/{Guid.NewGuid()}", new UpdateHouseholdRequest { Name = "X" })
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    // ---------------------------------------------------------------- households

    [Fact]
    public async Task RenameHousehold_ChangesName_AndEmptyNameIsRejectedWithAFriendlyMessage()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var householdId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Households.Add(new Household { Id = householdId, TenantId = tenantId, Name = "Alvarez" });
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var empty = await http.PutAsJsonAsync($"/api/households/{householdId}", new UpdateHouseholdRequest { Name = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("Enter a household name.", await empty.Content.ReadAsStringAsync());

        var ok = await http.PutAsJsonAsync($"/api/households/{householdId}", new UpdateHouseholdRequest { Name = " Alvarez Household " });
        ok.EnsureSuccessStatusCode();

        var households = await http.GetFromJsonAsync<List<HouseholdDto>>("/api/households");
        Assert.Equal("Alvarez Household", Assert.Single(households!).Name);
    }

    [Fact]
    public async Task CreateHousehold_EmptyName_IsRejectedWithAFriendlyMessage()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.PostAsJsonAsync("/api/households", new CreateHouseholdRequest { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Enter a household name.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteHousehold_UnlinksMembersButKeepsThemInPeople()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var householdId = Guid.NewGuid();
        var headId = Guid.NewGuid();
        var spouseId = Guid.NewGuid();
        await SeedHouseholdAsync(factory, tenantId, householdId, headId, spouseId);
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/households/{householdId}");

        response.EnsureSuccessStatusCode();
        Assert.Contains("still in People", (await response.ReadRequiredJsonAsync<MessageResponse>()).Message);
        Assert.Empty((await http.GetFromJsonAsync<List<HouseholdDto>>("/api/households"))!);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var people = await db.Clients.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ToListAsync();
        Assert.Equal(2, people.Count);
        Assert.All(people, p => Assert.Null(p.HouseholdId));
    }

    [Fact]
    public async Task RemoveHead_FromHousehold_ClearsHead_AndKeepsOtherMembers()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var householdId = Guid.NewGuid();
        var headId = Guid.NewGuid();
        var spouseId = Guid.NewGuid();
        await SeedHouseholdAsync(factory, tenantId, householdId, headId, spouseId);
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/households/{householdId}/members/{headId}");

        response.EnsureSuccessStatusCode();
        var household = Assert.Single((await http.GetFromJsonAsync<List<HouseholdDto>>("/api/households"))!);
        Assert.Null(household.PrimaryClientId);
        Assert.Null(household.PrimaryClientName);
        var member = Assert.Single(household.Members);
        Assert.Equal(spouseId, member.ClientId);

        var detail = await http.GetFromJsonAsync<ClientDetailDto>($"/api/clients/{headId}");
        Assert.Null(detail!.HouseholdId);
        Assert.Null(detail.HouseholdName);
    }

    [Fact]
    public async Task RemoveMember_NotInThatHousehold_Returns404()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var householdId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        await SeedHouseholdAsync(factory, tenantId, householdId, Guid.NewGuid(), Guid.NewGuid());
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(NewPerson(tenantId, outsiderId, "Out", "Sider"));
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await http.DeleteAsync($"/api/households/{householdId}/members/{outsiderId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HouseholdEndpoints_DoNotReachAnotherTenantsHousehold()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var otherTenant = Guid.NewGuid();
        var householdId = Guid.NewGuid();
        await SeedHouseholdAsync(factory, otherTenant, householdId, Guid.NewGuid(), Guid.NewGuid());
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Admin");

        Assert.Equal(HttpStatusCode.NotFound,
            (await http.PutAsJsonAsync($"/api/households/{householdId}", new UpdateHouseholdRequest { Name = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync($"/api/households/{householdId}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var household = await db.Households.IgnoreQueryFilters().SingleAsync(h => h.Id == householdId);
        Assert.Equal("Seeded Household", household.Name);
    }

    [Fact]
    public async Task AddingTheHeadToAnotherHousehold_ClearsTheOldHouseholdsHead()
    {
        var (factory, tenantId, actorId) = await NewWorkspaceAsync();
        using var _ = factory;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var headId = Guid.NewGuid();
        await SeedHouseholdAsync(factory, tenantId, firstId, headId, Guid.NewGuid());
        await factory.SeedAsync(db =>
        {
            db.Households.Add(new Household { Id = secondId, TenantId = tenantId, Name = "Second Household" });
            return Task.CompletedTask;
        });
        using var http = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        (await http.PostAsync($"/api/households/{secondId}/add-member/{headId}?role=Dependent", null)).EnsureSuccessStatusCode();

        var households = (await http.GetFromJsonAsync<List<HouseholdDto>>("/api/households"))!;
        Assert.Null(households.Single(h => h.Id == firstId).PrimaryClientId);
        Assert.Contains(households.Single(h => h.Id == secondId).Members, m => m.ClientId == headId && m.Role == HouseholdRole.Dependent);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<(GrowItApiFactory Factory, Guid TenantId, Guid ActorId)> NewWorkspaceAsync()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "People Test Org", ContactEmail = "org@example.test" });
            db.Users.Add(NewUser(tenantId, actorId, "Taylor", "Manager", "taylor@example.test"));
            return Task.CompletedTask;
        });
        return (factory, tenantId, actorId);
    }

    private static Task SeedHouseholdAsync(GrowItApiFactory factory, Guid tenantId, Guid householdId, Guid headId, Guid spouseId) =>
        factory.SeedAsync(db =>
        {
            db.Households.Add(new Household { Id = householdId, TenantId = tenantId, Name = "Seeded Household", PrimaryClientId = headId });
            var head = NewPerson(tenantId, headId, "Hana", "Head");
            head.HouseholdId = householdId;
            head.HouseholdRole = HouseholdRole.Head;
            var spouse = NewPerson(tenantId, spouseId, "Sam", "Spouse");
            spouse.HouseholdId = householdId;
            spouse.HouseholdRole = HouseholdRole.Spouse;
            db.Clients.AddRange(head, spouse);
            return Task.CompletedTask;
        });

    private static CreateClientRequest PersonRequest(string first, string last, DateTime? nextFollowUp) => new()
    {
        FirstName = first,
        LastName = last,
        HouseholdCount = 1,
        StabilityScore = 5,
        LifePhase = LifePhase.Crisis,
        NextFollowupDate = nextFollowUp
    };

    private static async Task<Guid> CreatePersonAsync(HttpClient http, string first, string last, DateTime? nextFollowUp)
    {
        var response = await http.PostAsJsonAsync("/api/clients", PersonRequest(first, last, nextFollowUp));
        response.EnsureSuccessStatusCode();
        return await response.ReadGuidPropertyAsync("clientId");
    }

    private static async Task<List<TaskListDto>> GetTasksAsync(HttpClient http) =>
        await http.GetFromJsonAsync<List<TaskListDto>>("/api/tasks?includeCompleted=true") ?? [];

    private static GrowIT.Core.Entities.Client NewPerson(Guid tenantId, Guid id, string first, string last) => new()
    {
        Id = id,
        TenantId = tenantId,
        FirstName = first,
        LastName = last,
        HouseholdCount = 1,
        StabilityScore = 5,
        LifePhase = LifePhase.Crisis
    };

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
