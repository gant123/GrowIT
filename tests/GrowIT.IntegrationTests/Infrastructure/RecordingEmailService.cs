using System.Collections.Concurrent;
using GrowIT.Core.Interfaces;

namespace GrowIT.Backend.Tests.Infrastructure;

/// <summary>
/// Test double for <see cref="IEmailService"/>: records every send attempt instead of calling Resend.
/// Set <see cref="FailSends"/> to make every send throw, like a Resend outage.
/// </summary>
public sealed class RecordingEmailService : IEmailService
{
    public sealed record SentEmail(string To, string Subject, string Body);

    private readonly ConcurrentQueue<SentEmail> _attempts = new();

    public bool FailSends { get; set; }

    /// <summary>Every send attempted, including ones that failed.</summary>
    public IReadOnlyList<SentEmail> Attempts => _attempts.ToArray();

    public Task SendEmailAsync(string to, string subject, string body)
    {
        _attempts.Enqueue(new SentEmail(to, subject, body));
        if (FailSends)
        {
            throw new InvalidOperationException("Simulated email provider failure.");
        }

        return Task.CompletedTask;
    }

    public async Task<EmailSendResult> SendEmailDetailedAsync(string to, string subject, string body)
    {
        try
        {
            await SendEmailAsync(to, subject, body);
            return new EmailSendResult { Succeeded = true, DeliveryMode = "Recorded", Message = "Recorded." };
        }
        catch (Exception ex)
        {
            return new EmailSendResult { Succeeded = false, DeliveryMode = "Failed", Message = "Email send failed.", Error = ex.Message };
        }
    }
}
