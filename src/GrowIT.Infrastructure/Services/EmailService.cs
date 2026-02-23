using GrowIT.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace GrowIT.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var result = await SendEmailInternalAsync(to, subject, body, throwOnFailure: true);
        if (!result.Succeeded && string.Equals(result.DeliveryMode, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(result.Error ?? "Email send failed.");
        }
    }

    public Task<EmailSendResult> SendEmailDetailedAsync(string to, string subject, string body) =>
        SendEmailInternalAsync(to, subject, body, throwOnFailure: false);

    private async Task<EmailSendResult> SendEmailInternalAsync(string to, string subject, string body, bool throwOnFailure)
    {
        var smtpHost = _config["Email:SmtpHost"]?.Trim();
        var smtpUser = _config["Email:SmtpUser"]?.Trim();
        var smtpPass = _config["Email:SmtpPass"];
        var fromEmail = _config["Email:FromEmail"]?.Trim();
        var smtpPort = ParseSmtpPort(_config["Email:SmtpPort"]);
        var useSsl = ResolveUseSsl(smtpHost, smtpPort);
        var timeoutMs = int.TryParse(_config["Email:TimeoutMs"], out var parsedTimeout)
            ? Math.Clamp(parsedTimeout, 1000, 120000)
            : 15000;

        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser) || 
            smtpUser.Contains("YOUR_") || (smtpPass != null && smtpPass.Contains("YOUR_")))
        {
            _logger.LogWarning("SMTP is not configured or using placeholders. Email to {To} with subject '{Subject}' will not be sent. Body: {Body}", to, subject, body);
            return new EmailSendResult
            {
                Succeeded = false,
                DeliveryMode = "SkippedUnconfigured",
                Message = "SMTP is not configured. Email was not sent."
            };
        }

        if (string.IsNullOrWhiteSpace(fromEmail))
        {
            fromEmail = smtpUser;
        }

        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = useSsl,
                Timeout = timeoutMs
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail!),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email sent to {To} successfully.", to);
            return new EmailSendResult
            {
                Succeeded = true,
                DeliveryMode = "Smtp",
                Message = "Email sent successfully via SMTP."
            };
        }
        catch (Exception ex)
        {
            if (ShouldUseDevelopmentFileFallback())
            {
                var filePath = await WriteDevelopmentEmailFileAsync(to, subject, body);
                _logger.LogWarning(ex,
                    "SMTP send failed in Development. Email written to file fallback at {FilePath}. To={To} Subject={Subject}",
                    filePath, to, subject);
                return new EmailSendResult
                {
                    Succeeded = true,
                    DeliveryMode = "DevFileFallback",
                    Message = "SMTP failed, but a development email file was generated.",
                    FallbackFilePath = filePath
                };
            }

            _logger.LogError(ex, "Failed to send email to {To}.", to);
            if (throwOnFailure)
                throw;

            return new EmailSendResult
            {
                Succeeded = false,
                DeliveryMode = "Failed",
                Message = "Email send failed.",
                Error = ex.Message
            };
        }
    }

    private int ParseSmtpPort(string? configuredPort)
    {
        if (int.TryParse(configuredPort, out var port) && port > 0 && port <= 65535)
            return port;

        return 587;
    }

    private bool ResolveUseSsl(string? smtpHost, int smtpPort)
    {
        var configured = _config["Email:UseSsl"];
        if (!string.IsNullOrWhiteSpace(configured) && bool.TryParse(configured, out var useSsl))
            return useSsl;

        return (smtpHost?.Contains("mailtrap", StringComparison.OrdinalIgnoreCase) ?? false)
            || smtpPort == 587
            || smtpPort == 465;
    }

    private bool ShouldUseDevelopmentFileFallback()
    {
        if (!string.Equals(_config["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase))
            return false;

        var configured = _config["Email:DevFileFallbackEnabled"];
        if (string.IsNullOrWhiteSpace(configured))
            return true;

        return bool.TryParse(configured, out var enabled) && enabled;
    }

    private async Task<string> WriteDevelopmentEmailFileAsync(string to, string subject, string body)
    {
        var folder = _config["Email:DevFileFallbackDirectory"];
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Path.Combine(AppContext.BaseDirectory, "dev-emails");
        }

        Directory.CreateDirectory(folder);

        var safeRecipient = string.Concat(to.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(folder, $"{timestamp}_{safeRecipient}.html");

        var html = new StringBuilder()
            .AppendLine("<!doctype html>")
            .AppendLine("<html><body style=\"font-family:Segoe UI,Arial,sans-serif\">")
            .AppendLine($"<h2>{WebUtility.HtmlEncode(subject)}</h2>")
            .AppendLine($"<p><strong>To:</strong> {WebUtility.HtmlEncode(to)}</p>")
            .AppendLine($"<p><strong>Saved:</strong> {DateTime.UtcNow:O} UTC</p>")
            .AppendLine("<hr/>")
            .AppendLine(body)
            .AppendLine("</body></html>")
            .ToString();

        await File.WriteAllTextAsync(filePath, html);
        return filePath;
    }
}
