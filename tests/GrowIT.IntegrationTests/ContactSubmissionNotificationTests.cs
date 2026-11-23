using System.Net;
using System.Net.Http.Json;
using GrowIT.Backend.Tests.Infrastructure;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrowIT.Backend.Tests;

/// <summary>
/// The public contact form (and every "Request Demo" button) posts to api/content/contact.
/// Each message is saved and, when Contact:NotifyEmails is set, emailed to those people.
/// </summary>
public class ContactSubmissionNotificationTests
{
    private static CreateContactSubmissionRequest DemoRequest() => new()
    {
        FullName = "Dana <script>alert('x')</script> Lee",
        Email = "dana@example.test",
        Organization = "Hope & Harvest",
        Subject = "Demo Request",
        Message = "Hi grow.IT team,\nI would like a live demo for <b>my</b> organization."
    };

    [Fact]
    public async Task SubmitContact_EmailsEachConfiguredRecipient_WithEncodedFieldsAndAdminLink()
    {
        var emails = new RecordingEmailService();
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            // Duplicates, spacing and either separator are tolerated.
            ["Contact:NotifyEmails"] = " sales@example.test, owner@example.test ;SALES@example.test,"
        }, emailService: emails);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/content/contact", DemoRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await response.Content.ReadFromJsonAsync<ContactSubmissionReceivedDto>();
        Assert.NotNull(received);
        await AssertSubmissionSavedAsync(factory, received!.Id);

        Assert.Equal(new[] { "sales@example.test", "owner@example.test" }, emails.Attempts.Select(a => a.To));

        var sent = emails.Attempts[0];
        Assert.Contains("Demo Request", sent.Subject);
        Assert.DoesNotContain('\n', sent.Subject);

        // Every visitor-supplied field is present, HTML-encoded.
        Assert.Contains("Dana &lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; Lee", sent.Body);
        Assert.Contains("dana@example.test", sent.Body);
        Assert.Contains("Hope &amp; Harvest", sent.Body);
        Assert.Contains("Demo Request", sent.Body);
        Assert.Contains("I would like a live demo for &lt;b&gt;my&lt;/b&gt; organization.", sent.Body);
        Assert.DoesNotContain("<script>", sent.Body);
        Assert.DoesNotContain("<b>my</b>", sent.Body);

        // Links to the admin page that lists submissions.
        Assert.Contains("https://localhost/super-admin/content", sent.Body);
    }

    [Fact]
    public async Task SubmitContact_StillSucceeds_WhenNotificationEmailFails()
    {
        var emails = new RecordingEmailService { FailSends = true };
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["Contact:NotifyEmails"] = "sales@example.test"
        }, emailService: emails);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/content/contact", DemoRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = await response.Content.ReadFromJsonAsync<ContactSubmissionReceivedDto>();
        Assert.NotNull(received);
        await AssertSubmissionSavedAsync(factory, received!.Id);
        Assert.Single(emails.Attempts);
    }

    [Fact]
    public async Task SubmitContact_SendsNothing_WhenNoRecipientsConfigured()
    {
        var emails = new RecordingEmailService();
        using var factory = new GrowItApiFactory(new Dictionary<string, string?>
        {
            ["Contact:NotifyEmails"] = ""
        }, emailService: emails);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/content/contact", DemoRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(emails.Attempts);
    }

    private static async Task AssertSubmissionSavedAsync(GrowItApiFactory factory, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.ContactSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        Assert.NotNull(saved);
        Assert.Equal("dana@example.test", saved!.Email);
    }
}
