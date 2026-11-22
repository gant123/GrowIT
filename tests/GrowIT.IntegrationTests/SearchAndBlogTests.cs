using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using ClientEntity = GrowIT.Core.Entities.Client;
using ProgramEntity = GrowIT.Core.Entities.Program;

namespace GrowIT.Backend.Tests;

public class SearchTests
{
    [Fact]
    public async Task Search_FindsPeopleProgramsAndMoneySources_InCurrentTenantOnly()
    {
        using var factory = new GrowItApiFactory();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var mariaId = Guid.NewGuid();

        await factory.SeedAsync(db =>
        {
            db.Clients.AddRange(
                NewClient(tenantA, "Maria", "Alvarez", mariaId),
                NewClient(tenantA, "Luis", "Ortega"),
                NewClient(tenantB, "Maria", "OtherTenant"));
            db.FamilyMembers.Add(new FamilyMember { TenantId = tenantA, ClientId = mariaId, FirstName = "Mariana", LastName = "Alvarez", Relationship = "Child" });
            db.Programs.AddRange(
                new ProgramEntity { TenantId = tenantA, Name = "Rent Relief", DefaultUnitCost = 100m },
                new ProgramEntity { TenantId = tenantB, Name = "Rent Relief", DefaultUnitCost = 100m });
            db.Funds.AddRange(
                new Fund { TenantId = tenantA, Name = "Marion County Grant", TotalAmount = 100m, AvailableAmount = 100m },
                new Fund { TenantId = tenantB, Name = "Marion County Grant", TotalAmount = 100m, AvailableAmount = 100m });
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantA, role: "Member");

        var people = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=maria");
        Assert.NotNull(people);
        var person = Assert.Single(people!, r => r.Type == SearchResultTypes.Person);
        Assert.Equal("Maria Alvarez", person.Title);
        Assert.Equal($"/clients/{mariaId}", person.Url);
        Assert.Contains(people!, r => r.Type == SearchResultTypes.HouseholdMember && r.Title == "Mariana Alvarez" && r.Url.StartsWith("/clients/member/"));
        Assert.DoesNotContain(people!, r => r.Title.Contains("OtherTenant"));

        var programs = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=RENT");
        var program = Assert.Single(programs!);
        Assert.Equal(SearchResultTypes.Program, program.Type);
        Assert.Equal("/programs", program.Url);

        var funds = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=county");
        var fund = Assert.Single(funds!);
        Assert.Equal(SearchResultTypes.MoneySource, fund.Type);
        Assert.Equal("/funds", fund.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("m")]
    [InlineData("  a  ")]
    public async Task Search_RequiresTwoCharacters(string query)
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Clients.Add(NewClient(tenantId, "Maria", "Alvarez"));
            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var results = await client.GetFromJsonAsync<List<SearchResultDto>>($"/api/search?q={Uri.EscapeDataString(query)}");

        Assert.NotNull(results);
        Assert.Empty(results!);
    }

    [Fact]
    public async Task Search_CapsPeopleResults()
    {
        using var factory = new GrowItApiFactory();
        var tenantId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            for (var i = 0; i < 12; i++)
            {
                db.Clients.Add(NewClient(tenantId, "Sam", $"Person{i:00}"));
            }

            return Task.CompletedTask;
        });

        using var client = factory.CreateTenantClient(tenantId);
        var results = await client.GetFromJsonAsync<List<SearchResultDto>>("/api/search?q=sam");

        Assert.Equal(5, results!.Count(r => r.Type == SearchResultTypes.Person));
    }

    private static ClientEntity NewClient(Guid tenantId, string first, string last, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        TenantId = tenantId,
        FirstName = first,
        LastName = last,
        HouseholdCount = 1,
        StabilityScore = 5,
        LifePhase = LifePhase.Stable
    };
}

public class PublicBlogTests
{
    [Fact]
    public async Task PublishedArticle_IsReadableBySlug_DraftsAndUnknownAreNotFound()
    {
        using var factory = new GrowItApiFactory();
        await factory.SeedAsync(db =>
        {
            db.BlogPosts.AddRange(
                NewPost("five-records-every-nonprofit-needs", "Five records every nonprofit needs", isPublished: true),
                NewPost("/legacy-leading-slash", "Legacy slug", isPublished: true),
                NewPost("unfinished-draft", "Draft", isPublished: false));
            return Task.CompletedTask;
        });

        using var anonymous = factory.CreateClient();

        using var published = await anonymous.GetAsync("/api/content/blog/five-records-every-nonprofit-needs");
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var article = await published.ReadRequiredJsonAsync<PublicBlogPostDto>();
        Assert.Equal("Five records every nonprofit needs", article.Title);
        Assert.Contains("<script>", article.Content); // stored as text; the page encodes it

        using var legacy = await anonymous.GetAsync("/api/content/blog/legacy-leading-slash");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal("legacy-leading-slash", (await legacy.ReadRequiredJsonAsync<PublicBlogPostDto>()).Slug);

        using var draft = await anonymous.GetAsync("/api/content/blog/unfinished-draft");
        Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);

        using var unknown = await anonymous.GetAsync("/api/content/blog/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var list = await anonymous.GetFromJsonAsync<List<PublicBlogPostDto>>("/api/content/blog");
        Assert.Equal(2, list!.Count);
        Assert.All(list, p => Assert.False(p.Slug.StartsWith('/')));
    }

    [Fact]
    public async Task AdminSave_NormalizesSlugWithoutLeadingSlash()
    {
        using var factory = new GrowItApiFactory();
        using var superAdmin = factory.CreateTenantClient(Guid.NewGuid(), role: "SuperAdmin");

        using var response = await superAdmin.PostAsJsonAsync("/api/admin/content/blog", new CreateBlogPostRequest
        {
            Title = "Five records",
            Slug = "/Five-Records-Every-Nonprofit/",
            Excerpt = "Short summary.",
            Content = "Body text.",
            PublishNow = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadRequiredJsonAsync<AdminBlogPostDto>();
        Assert.Equal("five-records-every-nonprofit", created.Slug);

        using var anonymous = factory.CreateClient();
        using var article = await anonymous.GetAsync("/api/content/blog/five-records-every-nonprofit");
        Assert.Equal(HttpStatusCode.OK, article.StatusCode);
    }

    private static BlogPost NewPost(string slug, string title, bool isPublished) => new()
    {
        Title = title,
        Slug = slug,
        Excerpt = "Excerpt",
        Content = "Intro paragraph.\n\n## Heading\n- one\n- two\n\n<script>alert('x')</script>",
        Category = "Operations",
        AuthorName = "GrowIT Team",
        IsPublished = isPublished,
        PublishedAt = isPublished ? DateTime.UtcNow.AddDays(-1) : null,
        CreatedAt = DateTime.UtcNow.AddDays(-2)
    };
}
