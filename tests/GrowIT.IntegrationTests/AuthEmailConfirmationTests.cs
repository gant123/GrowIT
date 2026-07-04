using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Identity;
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
        Assert.True(second?.Succeeded);
        Assert.True(second?.AlreadyConfirmed);
        Assert.Contains("already confirmed", second?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResendConfirmation_ReturnsAlreadyConfirmedMessage_WithoutPretendingNewEmailWasSent()
    {
        using var factory = new GrowItApiFactory();
        await CreateConfirmationUserAsync(factory, emailConfirmed: true);

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new ResendConfirmationEmailRequest
        {
            Email = "confirm@example.test"
        });

        response.EnsureSuccessStatusCode();
        var message = await response.Content.ReadFromJsonAsync<MessageResponse>();

        Assert.Contains("already confirmed", message?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confirmation link", message?.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
