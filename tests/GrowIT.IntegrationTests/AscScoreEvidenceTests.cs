using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using ClientEntity = GrowIT.Core.Entities.Client;
using ProgramEntity = GrowIT.Core.Entities.Program;

namespace GrowIT.Backend.Tests;

/// <summary>
/// Findings #68 and #45: typed links must not count as stored documents, and next steps /
/// unscored explanations must reflect the organization's actual data.
/// </summary>
public class AscScoreEvidenceTests
{
    [Fact]
    public async Task TypedLinks_DoNotCountAsDocuments_OrProduceAScore()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            // Profile complete but nothing else except link-style "documents" (what the audit typed).
            db.Tenants.Add(CompleteTenant(tenantId));
            for (var i = 0; i < 5; i++)
            {
                db.Documents.Add(new Document
                {
                    TenantId = tenantId,
                    ClientId = i == 0 ? clientId : null,
                    FileUrl = i == 0 ? "my lease.pdf" : $"https://example.org/doc-{i}",
                    FileType = "PDF",
                    Category = DocumentCategory.Other
                });
            }

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var score = await (await client.GetAsync("/api/organization-readiness/asc-score")).ReadRequiredJsonAsync<AscScoreDto>();

        Assert.Equal(0, score.Evidence.Documents);
        Assert.Equal(0, score.Evidence.OrganizationDocuments);
        Assert.Equal(5, score.Evidence.LinkOnlyDocuments);
        Assert.False(score.IsScored);
        Assert.Null(score.Score);
        Assert.Contains(score.ScoreRequirements, r => r.Contains("uploaded document", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UploadedDocuments_CountAsEvidence()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(CompleteTenant(tenantId));
            db.Documents.Add(new Document
            {
                TenantId = tenantId,
                ClientId = null,
                FileType = "PDF",
                Category = DocumentCategory.Legal,
                StoragePath = $"{tenantId:N}/{Guid.NewGuid():N}.pdf",
                OriginalFileName = "irs-letter.pdf",
                ContentType = "application/pdf",
                SizeBytes = 2048,
                UploadedAt = DateTime.UtcNow
            });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var score = await (await client.GetAsync("/api/organization-readiness/asc-score")).ReadRequiredJsonAsync<AscScoreDto>();

        Assert.Equal(1, score.Evidence.Documents);
        Assert.Equal(1, score.Evidence.OrganizationDocuments);
        Assert.Equal(0, score.Evidence.LinkOnlyDocuments);
        Assert.True(score.IsScored);
        Assert.Empty(score.ScoreRequirements);
        Assert.Contains(score.Pillars, p => p.Key == "legal-compliance" &&
            p.Evidence.Any(e => e.Contains("organization document", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Unscored_NextSteps_SkipWhatAlreadyExists_AndNameMissingProfileFields()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            // Program and money source exist, but the profile has no address or contact email.
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Half Set Up Org",
                OrganizationType = "501c3",
                CreatedAt = DateTime.UtcNow
            });
            db.Programs.Add(new ProgramEntity { TenantId = tenantId, Name = "Food Pantry", DefaultUnitCost = 25m });
            db.Funds.Add(new Fund { TenantId = tenantId, Name = "Community Donors", TotalAmount = 1000m, AvailableAmount = 1000m });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var score = await (await client.GetAsync("/api/organization-readiness/asc-score")).ReadRequiredJsonAsync<AscScoreDto>();

        Assert.False(score.IsScored);
        Assert.Equal("Unscored", score.ScoreStatus);

        Assert.DoesNotContain(score.NextSteps, s => s.Contains("program", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(score.NextSteps, s => s.Contains("money source", StringComparison.OrdinalIgnoreCase));

        var profileRequirement = Assert.Single(score.ScoreRequirements);
        Assert.Contains("contact email", profileRequirement);
        Assert.Contains("address", profileRequirement);
        Assert.DoesNotContain("organization type", profileRequirement);
        Assert.Contains(score.NextSteps, s => s.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unscored_NextSteps_ListTheFoundationThatIsActuallyMissing()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Brand New Org", CreatedAt = DateTime.UtcNow });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var score = await (await client.GetAsync("/api/organization-readiness/asc-score")).ReadRequiredJsonAsync<AscScoreDto>();

        Assert.False(score.IsScored);
        Assert.Contains(score.NextSteps, s => s.Contains("first program", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(score.NextSteps, s => s.Contains("first money source", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, score.ScoreRequirements.Count);
    }

    [Fact]
    public async Task Scored_NextSteps_DoNotAskForProgramsOrMoneySourcesThatExist()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(CompleteTenant(tenantId));
            db.Clients.Add(new ClientEntity
            {
                TenantId = tenantId,
                FirstName = "Maria",
                LastName = "Alvarez",
                HouseholdCount = 1,
                StabilityScore = 5,
                LifePhase = LifePhase.Stable
            });
            db.Programs.Add(new ProgramEntity { TenantId = tenantId, Name = "Rent Help", DefaultUnitCost = 300m });
            db.Funds.Add(new Fund { TenantId = tenantId, Name = "General Fund", TotalAmount = 5000m, AvailableAmount = 5000m });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var score = await (await client.GetAsync("/api/organization-readiness/asc-score")).ReadRequiredJsonAsync<AscScoreDto>();

        Assert.True(score.IsScored);
        Assert.DoesNotContain(score.NextSteps, s => s.Contains("first program", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(score.NextSteps, s => s.Contains("first money source", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(score.NextSteps, s => s.Contains("programs, funds", StringComparison.OrdinalIgnoreCase));
    }

    private static Tenant CompleteTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        Name = "Ready Org",
        Address = "1 Main St",
        ContactEmail = "hello@ready.local",
        OrganizationType = "501c3",
        OrganizationSize = "1-10",
        CreatedAt = DateTime.UtcNow
    };
}
