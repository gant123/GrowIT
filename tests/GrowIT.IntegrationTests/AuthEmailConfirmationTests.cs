using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

public class AuthEmailConfirmationTests
{
    [Fact]
    public async Task ConfirmEmail_IsIdempotent_WhenLinkIsClickedMoreThanOnce()
    {
        using var factory = new GrowItApiFactory();
        var user = await CreateConfirmationUserAsync(factory, emailConfirmed: false);

        string token;
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        }

        using var client = factory.CreateClient();
        var endpoint = BuildConfirmEndpoint(user.Id, token);

        var firstResponse = await client.GetAsync(endpoint);
        firstResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<ConfirmEmailResultDto>();

        var secondResponse = await client.GetAsync(endpoint);
        secondResponse.EnsureSuccessStatusCode();
        var second = await secondResponse.Content.ReadFromJsonAsync<ConfirmEmailResultDto>();

        Assert.True(first?.Succeeded);
        Assert.False(first?.AlreadyConfirmed);
        Assert.Equal("confirm@example.test", first?.Email);
        Assert.True(second?.Succeeded);
        Assert.True(second?.AlreadyConfirmed);
        Assert.Equal("confirm@example.test", second?.Email);
        Assert.Contains("already confirmed", second?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResendConfirmation_ReturnsIdenticalResponse_ForKnownAndUnknownEmails()
    {
        // Anti-enumeration invariant: the response must not reveal whether an account
        // exists or what state it is in — known/confirmed, known/unconfirmed, and
        // unknown emails all get the same answer.
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Email:DevFileFallbackEnabled"] = "true",
            ["Email:DevFileFallbackDirectory"] = "/tmp/growit-test-emails"
        });
        await CreateConfirmationUserAsync(factory, emailConfirmed: true);

        using var client = factory.CreateClient();

        var knownResponse = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new ResendConfirmationEmailRequest
        {
            Email = "confirm@example.test"
        });
        knownResponse.EnsureSuccessStatusCode();
        var knownMessage = await knownResponse.Content.ReadFromJsonAsync<MessageResponse>();

        var unknownResponse = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new ResendConfirmationEmailRequest
        {
            Email = "nobody-here@example.test"
        });
        unknownResponse.EnsureSuccessStatusCode();
        var unknownMessage = await unknownResponse.Content.ReadFromJsonAsync<MessageResponse>();

        Assert.False(string.IsNullOrWhiteSpace(knownMessage?.Message));
        Assert.Equal(knownMessage!.Message, unknownMessage?.Message);
    }

    [Fact]
    public async Task ResendConfirmation_ThrottlesRepeatedRequests_ForUnconfirmedUser()
    {
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Email:DevFileFallbackEnabled"] = "true",
            ["Email:DevFileFallbackDirectory"] = "/tmp/growit-test-emails"
        });
        var user = await CreateConfirmationUserAsync(factory, emailConfirmed: false);

        using var client = factory.CreateClient();
        var request = new ResendConfirmationEmailRequest
        {
            Email = "confirm@example.test"
        };

        var firstResponse = await client.PostAsJsonAsync("/api/auth/resend-confirmation", request);
        firstResponse.EnsureSuccessStatusCode();
        var firstMessage = await firstResponse.Content.ReadFromJsonAsync<MessageResponse>();

        var secondResponse = await client.PostAsJsonAsync("/api/auth/resend-confirmation", request);
        secondResponse.EnsureSuccessStatusCode();
        var secondMessage = await secondResponse.Content.ReadFromJsonAsync<MessageResponse>();

        // The cooldown must hold (only one email actually sent, asserted below via the DB),
        // but the response may not reveal it — both calls get the same neutral answer.
        Assert.Equal(firstMessage?.Message, secondMessage?.Message);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedUser = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);

        Assert.Equal(1, storedUser.ConfirmationEmailSendCount);
        Assert.NotNull(storedUser.LastConfirmationEmailSentAt);
    }

    [Fact]
    public async Task ForgotPassword_ThrottlesRepeatedRequests_ForSameUser()
    {
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Email:DevFileFallbackEnabled"] = "true",
            ["Email:DevFileFallbackDirectory"] = "/tmp/growit-test-emails"
        });
        var user = await CreateConfirmationUserAsync(factory, emailConfirmed: true);

        using var client = factory.CreateClient();
        var request = new ForgotPasswordRequest
        {
            Email = "confirm@example.test"
        };

        var firstResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", request);
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", request);
        secondResponse.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storedUser = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);

        Assert.Equal(1, storedUser.PasswordResetEmailSendCount);
        Assert.NotNull(storedUser.LastPasswordResetEmailSentAt);
    }

    private static async Task<User> CreateConfirmationUserAsync(GrowItApiFactory factory, bool emailConfirmed)
    {
        var tenantId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FirstName = "Confirm",
            LastName = "User",
            Email = "confirm@example.test",
            UserName = "confirm@example.test",
            NormalizedEmail = "CONFIRM@EXAMPLE.TEST",
            NormalizedUserName = "CONFIRM@EXAMPLE.TEST",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            IsActive = true,
            EmailConfirmed = emailConfirmed,
            CreatedAt = DateTime.UtcNow
        };

        await factory.SeedAsync(db =>
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Confirmation Test Org"
            });
            db.Users.Add(user);

            return Task.CompletedTask;
        });

        return user;
    }

    private static string BuildConfirmEndpoint(Guid userId, string token) =>
        $"/api/auth/confirm-email?userId={Uri.EscapeDataString(userId.ToString())}&token={Uri.EscapeDataString(token)}";
}
