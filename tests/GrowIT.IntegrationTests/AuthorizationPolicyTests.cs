using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GrowIT.Infrastructure.Data;

namespace GrowIT.Backend.Tests;

public class AuthorizationPolicyTests
{
    [Fact]
    public async Task AdminOnly_SeedDemoData_RejectsManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Manager");

        var response = await client.PostAsJsonAsync("/api/admin/seed-demo-data", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOnly_UserList_RejectsManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Manager");

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_GrowthPlansCreate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/growthplans", new
        {
            personId = Guid.NewGuid(),
            title = "Test Plan",
            season = "Q1"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_ImprintCreate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/imprints", new
        {
            clientId = Guid.NewGuid(),
            title = "Outcome",
            category = 0,
            outcome = 0,
            dateOccurred = DateTime.UtcNow,
            notes = "Test"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOrManager_InvestmentApprove_RejectsCaseManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Case Manager");

        var response = await client.PostAsJsonAsync($"/api/investments/{Guid.NewGuid()}/approve", new
        {
            approvedBy = "tester"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminOrManager_ReportsGenerate_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/reports/generate", new
        {
            reportType = "readiness",
            format = "pdf"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_EmailDiagnostics_RejectsAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.GetAsync("/api/admin/email-diagnostics");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_EmailDiagnostics_AllowsSuperAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.GetAsync("/api/admin/email-diagnostics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_SiteContent_RejectsOwner()
    {
        // Owner is a per-tenant role and must NOT inherit platform/site-wide SuperAdmin access.
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Owner");

        var response = await client.GetAsync("/api/admin/content/blog");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_FeedbackReview_RejectsTenantAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.GetAsync("/api/admin/feedback");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_FeedbackReview_RejectsManager()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Manager");

        var response = await client.GetAsync("/api/admin/feedback");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_FeedbackReview_AllowsSuperAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.GetAsync("/api/admin/feedback");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdminOnly_FeedbackReview_ReturnsPlatformFeedbackAcrossTenants()
    {
        var feedbackTenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.BetaFeedbacks.Add(new BetaFeedback
            {
                TenantId = feedbackTenantId,
                UserId = Guid.NewGuid(),
                Category = "Bug",
                Severity = "High",
                Title = "Cross-tenant feedback",
                Message = "This belongs to the platform backlog.",
                PageUrl = "/clients",
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var items = await client.GetFromJsonAsync<List<BetaFeedbackListItemDto>>("/api/admin/feedback");

        Assert.Contains(items ?? [], item =>
            item.Title == "Cross-tenant feedback" &&
            item.TenantId == feedbackTenantId);
    }

    [Fact]
    public async Task UpdateUserRole_RejectsEscalationToSuperAdminByAdmin()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Admin");

        var response = await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new
        {
            role = "SuperAdmin"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_CreateClient_RejectsAnalyst()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Analyst");

        var response = await client.PostAsJsonAsync("/api/clients", new { firstName = "A", lastName = "B" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_CreateHousehold_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsJsonAsync("/api/households", new { name = "Smith Household" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_AddFamilyMember_RejectsAnalyst()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Analyst");

        var response = await client.PostAsJsonAsync($"/api/clients/{Guid.NewGuid()}/members", new
        {
            firstName = "Kid",
            lastName = "Smith",
            relationship = "Child"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_UpdateFamilyMember_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PutAsJsonAsync($"/api/clients/members/{Guid.NewGuid()}", new
        {
            firstName = "Kid",
            lastName = "Smith",
            relationship = "Child"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_DeleteFamilyMember_RejectsAnalyst()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Analyst");

        var response = await client.DeleteAsync($"/api/clients/members/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ServiceWriter_AddHouseholdMember_RejectsMember()
    {
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "Member");

        var response = await client.PostAsync(
            $"/api/households/{Guid.NewGuid()}/add-member/{Guid.NewGuid()}?role=Head",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SeedDemoData_AllowsSuperAdmin_ButReportsNotImplemented()
    {
        // SuperAdmin satisfies AdminOnly (superset), so it passes the policy and reaches the
        // action, which is an honest 501 stub rather than a fake success.
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.PostAsJsonAsync("/api/admin/seed-demo-data", new { });

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUserRole_AllowsSuperAdminToGrantElevatedRole()
    {
        // A SuperAdmin passes the role-assignment gate; the request then fails only because
        // the target user does not exist (404), proving the elevated role was permitted.
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        var response = await client.PutAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/role", new
        {
            role = "SuperAdmin"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_UserList_ReturnsUsersAcrossOrganizationsWithOrganizationName()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.AddRange(
                new Tenant { Id = tenantA, Name = "Alpha Org" },
                new Tenant { Id = tenantB, Name = "Beta Org" });
            db.Users.AddRange(
                NewAdminTestUser(tenantA, "alpha@example.test", "Alpha", "User"),
                NewAdminTestUser(tenantB, "beta@example.test", "Beta", "User"));

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantA, role: "SuperAdmin");

        var users = await client.GetFromJsonAsync<List<AdminUserListItemDto>>("/api/admin/users");

        Assert.Contains(users ?? [], u => u.Email == "alpha@example.test" && u.OrganizationName == "Alpha Org");
        Assert.Contains(users ?? [], u => u.Email == "beta@example.test" && u.OrganizationName == "Beta Org");
    }

    [Fact]
    public async Task TenantAdmin_UserList_StaysScopedToOwnOrganization()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.AddRange(
                new Tenant { Id = tenantA, Name = "Alpha Org" },
                new Tenant { Id = tenantB, Name = "Beta Org" });
            db.Users.AddRange(
                NewAdminTestUser(tenantA, "alpha@example.test", "Alpha", "User"),
                NewAdminTestUser(tenantB, "beta@example.test", "Beta", "User"));

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantA, role: "Admin");

        var users = await client.GetFromJsonAsync<List<AdminUserListItemDto>>("/api/admin/users");

        Assert.Contains(users ?? [], u => u.Email == "alpha@example.test");
        Assert.DoesNotContain(users ?? [], u => u.Email == "beta@example.test");
    }

    [Fact]
    public async Task SuperAdmin_CanChangeAndDeactivateUserInAnotherOrganization()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.AddRange(
                new Tenant { Id = tenantA, Name = "Alpha Org" },
                new Tenant { Id = tenantB, Name = "Beta Org" });
            db.Users.Add(NewAdminTestUser(tenantB, "target@example.test", "Target", "User", targetUserId));

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantA, role: "SuperAdmin");

        var roleResponse = await client.PutAsJsonAsync($"/api/admin/users/{targetUserId}/role", new UpdateAdminUserRoleRequest
        {
            Role = "Manager"
        });
        roleResponse.EnsureSuccessStatusCode();
        var rolePayload = await roleResponse.Content.ReadFromJsonAsync<AdminUserListItemDto>();

        Assert.Equal("Manager", rolePayload?.Role);
        Assert.Equal("Beta Org", rolePayload?.OrganizationName);

        var deactivateResponse = await client.PostAsJsonAsync($"/api/admin/users/{targetUserId}/deactivate", new { });
        deactivateResponse.EnsureSuccessStatusCode();
        var deactivatePayload = await deactivateResponse.Content.ReadFromJsonAsync<AdminUserListItemDto>();

        Assert.False(deactivatePayload?.IsActive);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == targetUserId);
        Assert.False(user.IsActive);
    }

    private static User NewAdminTestUser(Guid tenantId, string email, string firstName, string lastName, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        UserName = email,
        NormalizedEmail = email.ToUpperInvariant(),
        NormalizedUserName = email.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        IsActive = true,
        EmailConfirmed = true,
        CreatedAt = DateTime.UtcNow
    };
}
