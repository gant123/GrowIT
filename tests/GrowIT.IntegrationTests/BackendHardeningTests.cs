using System.Net.Http.Json;
using System.Net;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

public class BackendHardeningTests
{
    [Fact]
    public async Task AuditLog_RedactsSensitiveClientFields()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "SuperAdmin");

        var response = await client.PostAsJsonAsync("/api/clients", new CreateClientRequest
        {
            FirstName = "Sensitive",
            LastName = "Client",
            SSNLast4 = "1234",
            HouseholdCount = 1,
            StabilityScore = 5,
            LifePhase = LifePhase.Crisis
        });

        response.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && (a.TableName == "Clients" || a.TableName == "Client"))
            .OrderByDescending(a => a.CreatedAt)
            .FirstAsync();

        Assert.DoesNotContain("1234", audit.NewData ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", audit.NewData ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportRunDetails_DoesNotExposeRawRequestPayload()
    {
        var tenantId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();

        await factory.SeedAsync(db =>
        {
            db.ReportRuns.Add(new ReportRun
            {
                Id = reportId,
                TenantId = tenantId,
                Name = "Impact Summary",
                ReportType = ReportContract.ImpactSummary,
                Format = "pdf",
                RequestPayloadJson = """{"reportType":"impact-summary","internalNote":"do not expose"}""",
                Status = "Generated",
                GeneratedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            });

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId, role: "Admin");
        var response = await client.GetAsync($"/api/reports/{reportId}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("requestPayloadJson", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalNote", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("impact-summary", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateInvestment_RejectsInvalidDtoBeforeWriting()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/investments", new CreateInvestmentRequest
        {
            ClientId = Guid.Empty,
            FundId = Guid.Empty,
            ProgramId = Guid.Empty,
            Amount = 0m,
            Reason = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Investments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task GenerateReport_RejectsUnsupportedFormat()
    {
        var tenantId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, role: "Admin");

        var response = await client.PostAsJsonAsync("/api/reports/generate", new GenerateReportRequest
        {
            ReportType = ReportContract.ImpactSummary,
            Format = "xml"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GenerateReport_ReusesRecentMatchingRun()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, userId, role: "Admin");

        var request = new GenerateReportRequest
        {
            ReportType = ReportContract.ImpactSummary,
            Format = "pdf"
        };

        var first = await client.PostAsJsonAsync("/api/reports/generate", request);
        var second = await client.PostAsJsonAsync("/api/reports/generate", request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstReport = await first.Content.ReadFromJsonAsync<RecentReport>();
        var secondReport = await second.Content.ReadFromJsonAsync<RecentReport>();

        Assert.NotNull(firstReport);
        Assert.NotNull(secondReport);
        Assert.Equal(firstReport!.Id, secondReport!.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reportRuns = await db.ReportRuns.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.RequestedByUserId == userId)
            .ToListAsync();

        Assert.Single(reportRuns);
        Assert.False(string.IsNullOrWhiteSpace(reportRuns[0].RequestFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(reportRuns[0].IdempotencyKey));
    }

    [Fact]
    public async Task SubmitFeedback_ReusesRecentMatchingSubmission()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var factory = new GrowItApiFactory();
        using var client = factory.CreateTenantClient(tenantId, userId, role: "Admin");

        var request = new CreateBetaFeedbackRequest
        {
            Category = "Bug",
            Severity = "High",
            Title = "Email button issue",
            Message = "The confirmation button was clicked twice.",
            PageUrl = "/confirm-email"
        };

        var first = await client.PostAsJsonAsync("/api/feedback", request);
        var second = await client.PostAsJsonAsync("/api/feedback", request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstFeedback = await first.Content.ReadFromJsonAsync<BetaFeedbackListItemDto>();
        var secondFeedback = await second.Content.ReadFromJsonAsync<BetaFeedbackListItemDto>();

        Assert.NotNull(firstFeedback);
        Assert.NotNull(secondFeedback);
        Assert.Equal(firstFeedback!.Id, secondFeedback!.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var feedbackItems = await db.BetaFeedbacks.IgnoreQueryFilters()
            .Where(f => f.UserId == userId)
            .ToListAsync();

        Assert.Single(feedbackItems);
        Assert.False(string.IsNullOrWhiteSpace(feedbackItems[0].SubmissionFingerprint));
        Assert.False(string.IsNullOrWhiteSpace(feedbackItems[0].IdempotencyKey));
    }

    [Fact]
    public async Task SendTestEmail_ThrottlesRepeatedSuccessfulSend()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Email:DevFileFallbackEnabled"] = "true",
            ["Email:DevFileFallbackDirectory"] = "/tmp/growit-test-emails"
        });
        using var client = factory.CreateTenantClient(tenantId, userId, role: "SuperAdmin");

        var request = new SendTestEmailRequest
        {
            ToEmail = $"test-{Guid.NewGuid():N}@example.com"
        };

        var first = await client.PostAsJsonAsync("/api/admin/email-test", request);
        var second = await client.PostAsJsonAsync("/api/admin/email-test", request);

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<SendTestEmailResponse>();
        var secondResult = await second.Content.ReadFromJsonAsync<SendTestEmailResponse>();

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.True(firstResult!.Succeeded);
        Assert.False(secondResult!.Succeeded);
        Assert.Equal("Throttled", secondResult.DeliveryMode);
        Assert.Contains("already sent recently", secondResult.Message, StringComparison.OrdinalIgnoreCase);
    }
}
