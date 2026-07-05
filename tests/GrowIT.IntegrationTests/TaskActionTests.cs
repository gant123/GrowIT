using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

public class TaskActionTests
{
    [Fact]
    public async Task CaseManager_CanLoadTaskAssignees()
    {
        var tenantId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(NewTenant(tenantId));
            db.Users.Add(NewUser(tenantId, assigneeId, "Avery", "Caseworker", "avery@example.test"));
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId, role: "Case Manager");

        var assignees = await client.GetFromJsonAsync<List<TaskAssigneeDto>>("/api/tasks/assignees");

        Assert.Contains(assignees ?? [], user =>
            user.Id == assigneeId &&
            user.FullName == "Avery Caseworker" &&
            user.IsActive);
    }

    [Fact]
    public async Task CreateTask_ListsAssignedUserCreatorAndType()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedTaskSetupAsync(factory, tenantId, actorId, assigneeId, clientId);
        using var client = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var createResponse = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest
        {
            ClientId = clientId,
            AssignedTo = assigneeId,
            Type = ActionItemType.Documentation,
            DueDate = DateTime.UtcNow.Date.AddDays(3),
            Notes = "Collect board approval letter."
        });

        createResponse.EnsureSuccessStatusCode();

        var tasks = await client.GetFromJsonAsync<List<TaskListDto>>("/api/tasks?includeCompleted=true");

        var item = Assert.Single(tasks ?? []);
        Assert.Equal(clientId, item.ClientId);
        Assert.Equal(assigneeId, item.AssignedTo);
        Assert.Equal("Avery Caseworker", item.AssignedToName);
        Assert.Equal(actorId, item.CreatedByUserId);
        Assert.Equal("Taylor Manager", item.CreatedByName);
        Assert.Equal(ActionItemType.Documentation, item.Type);
        Assert.Equal(GrowIT.Shared.Enums.TaskStatus.Pending, item.Status);
        Assert.Null(item.CompletedAt);
    }

    [Fact]
    public async Task UpdateTaskStatus_ReturnsDtoAndSetsCompletedAt()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedTaskSetupAsync(factory, tenantId, actorId, assigneeId, clientId, db =>
        {
            db.Tasks.Add(new AppTask
            {
                Id = taskId,
                TenantId = tenantId,
                ClientId = clientId,
                AssignedTo = assigneeId,
                CreatedByUserId = actorId,
                Type = ActionItemType.ClientFollowUp,
                DueDate = DateTime.UtcNow.Date.AddDays(1),
                Status = GrowIT.Shared.Enums.TaskStatus.Pending,
                Notes = "Call about next step.",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        });
        using var client = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await client.PatchAsJsonAsync($"/api/tasks/{taskId}/status", new UpdateTaskStatusRequest
        {
            Status = GrowIT.Shared.Enums.TaskStatus.Completed
        });

        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<TaskListDto>();

        Assert.NotNull(updated);
        Assert.Equal(GrowIT.Shared.Enums.TaskStatus.Completed, updated!.Status);
        Assert.NotNull(updated.CompletedAt);
        Assert.NotNull(updated.UpdatedAt);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == taskId);
        Assert.Equal(GrowIT.Shared.Enums.TaskStatus.Completed, stored.Status);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task DeleteTask_RemovesTask()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await SeedTaskSetupAsync(factory, tenantId, actorId, assigneeId, clientId, db =>
        {
            db.Tasks.Add(new AppTask
            {
                Id = taskId,
                TenantId = tenantId,
                ClientId = clientId,
                AssignedTo = assigneeId,
                Type = ActionItemType.Administrative,
                DueDate = DateTime.UtcNow.Date.AddDays(1),
                Status = GrowIT.Shared.Enums.TaskStatus.Pending,
                Notes = "Internal checklist item.",
                CreatedAt = DateTime.UtcNow
            });
        });
        using var client = factory.CreateTenantClient(tenantId, actorId, role: "Case Manager");

        var response = await client.DeleteAsync($"/api/tasks/{taskId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Tasks.IgnoreQueryFilters().AnyAsync(t => t.Id == taskId));
    }

    private static Task SeedTaskSetupAsync(
        GrowItApiFactory factory,
        Guid tenantId,
        Guid actorId,
        Guid assigneeId,
        Guid clientId,
        Action<ApplicationDbContext>? extraSeed = null)
    {
        return factory.SeedAsync(db =>
        {
            db.Tenants.Add(NewTenant(tenantId));
            db.Users.Add(NewUser(tenantId, actorId, "Taylor", "Manager", "taylor@example.test"));
            db.Users.Add(NewUser(tenantId, assigneeId, "Avery", "Caseworker", "avery@example.test"));
            db.Clients.Add(new GrowIT.Core.Entities.Client
            {
                Id = clientId,
                TenantId = tenantId,
                FirstName = "Jordan",
                LastName = "Rivera",
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Stable
            });
            extraSeed?.Invoke(db);
            return Task.CompletedTask;
        });
    }

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Task Test Org",
        ContactEmail = "org@example.test"
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
