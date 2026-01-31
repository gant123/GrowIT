using GrowIT.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

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
        var smtpHost = _config["Email:SmtpHost"];
        var smtpPort = int.Parse(_config["Email:SmtpPort"] ?? "587");
        var smtpUser = _config["Email:SmtpUser"];
        var smtpPass = _config["Email:SmtpPass"];
        var fromEmail = _config["Email:FromEmail"];

        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(smtpUser) || 
            smtpUser.Contains("YOUR_") || (smtpPass != null && smtpPass.Contains("YOUR_")))
        {
            _logger.LogWarning("SMTP is not configured or using placeholders. Email to {To} with subject '{Subject}' will not be sent. Body: {Body}", to, subject, body);
            return;
        }

        try
        {
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPass),
                EnableSsl = smtpHost.Contains("mailtrap") || smtpPort == 587 || smtpPort == 465
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail ?? smtpUser!),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(to);

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email sent to {To} successfully.", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}.", to);
            throw;
        }
    }
}
