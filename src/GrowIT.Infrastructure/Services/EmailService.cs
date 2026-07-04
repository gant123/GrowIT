using GrowIT.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GrowIT.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var result = await SendEmailInternalAsync(to, subject, body, throwOnFailure: true);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error ?? "Email send failed.");
        }
    }

    public Task<EmailSendResult> SendEmailDetailedAsync(string to, string subject, string body) =>
        SendEmailInternalAsync(to, subject, body, throwOnFailure: false);

    private async Task<EmailSendResult> SendEmailInternalAsync(string to, string subject, string body, bool throwOnFailure)
    {
        var apiKey = ResolveResendApiKey();
        var from = ResolveFromAddress();
        var apiUrl = ResolveResendApiUrl();
        var timeoutMs = int.TryParse(_config["Email:TimeoutMs"], out var parsedTimeout)
            ? Math.Clamp(parsedTimeout, 1000, 120000)
            : 15000;

        if (string.IsNullOrWhiteSpace(apiKey) || IsPlaceholder(apiKey))
        {
            if (ShouldUseDevelopmentFileFallback())
            {
                var filePath = await WriteDevelopmentEmailFileAsync(to, subject, body);
                _logger.LogWarning(
                    "Resend API key is not configured. Email written to development fallback at {FilePath}. To={To} Subject={Subject}",
                    filePath, to, subject);
                return new EmailSendResult
                {
                    Succeeded = true,
                    DeliveryMode = "DevFileFallback",
                    Message = "Resend is not configured, but a development email file was generated.",
                    FallbackFilePath = filePath
                };
            }

            var message = "Resend API key is not configured. Email was not sent.";
            _logger.LogWarning("{Message} To={To} Subject={Subject}", message, to, subject);
            if (throwOnFailure)
                throw new InvalidOperationException(message);

            return new EmailSendResult
            {
                Succeeded = false,
                DeliveryMode = "SkippedUnconfigured",
                Message = message,
                Error = message
            };
        }

        if (string.IsNullOrWhiteSpace(from))
        {
            var message = "Email:FromEmail is required for Resend delivery.";
            _logger.LogWarning("{Message} To={To} Subject={Subject}", message, to, subject);
            if (throwOnFailure)
                throw new InvalidOperationException(message);

            return new EmailSendResult
            {
                Succeeded = false,
                DeliveryMode = "SkippedUnconfigured",
                Message = message,
                Error = message
            };
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = JsonContent.Create(new
            {
                from,
                to = new[] { to },
                subject,
                html = body
            });

            using var response = await client.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var message = $"Resend email send failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
                _logger.LogError("{Message} Response={ResponseBody} To={To} Subject={Subject}", message, responseBody, to, subject);
                if (throwOnFailure)
                    throw new InvalidOperationException($"{message} {ExtractResendError(responseBody)}".Trim());

                return new EmailSendResult
                {
                    Succeeded = false,
                    DeliveryMode = "Resend",
                    Message = "Email send failed via Resend.",
                    Error = $"{message} {ExtractResendError(responseBody)}".Trim()
                };
            }

            var resendId = ExtractResendId(responseBody);
            _logger.LogInformation("Email sent to {To} successfully via Resend. ResendId={ResendId}", to, resendId);
            return new EmailSendResult
            {
                Succeeded = true,
                DeliveryMode = "Resend",
                Message = string.IsNullOrWhiteSpace(resendId)
                    ? "Email sent successfully via Resend."
                    : $"Email sent successfully via Resend. Id: {resendId}"
            };
        }
        catch (Exception ex)
        {
            if (ShouldUseDevelopmentFileFallback())
            {
                var filePath = await WriteDevelopmentEmailFileAsync(to, subject, body);
                _logger.LogWarning(ex,
                    "Resend send failed in Development. Email written to file fallback at {FilePath}. To={To} Subject={Subject}",
                    filePath, to, subject);
                return new EmailSendResult
                {
                    Succeeded = true,
                    DeliveryMode = "DevFileFallback",
                    Message = "Resend failed, but a development email file was generated.",
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

    private string? ResolveResendApiKey()
    {
        var apiKey = _config["Email:ResendApiKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        apiKey = _config["Resend:ApiKey"]?.Trim();
        if (!string.IsNullOrWhiteSpace(apiKey))
            return apiKey;

        // Temporary rollout compatibility for environments that stored the Resend key
        // as the SMTP password when using smtp.resend.com.
        var smtpHost = _config["Email:SmtpHost"]?.Trim();
        var smtpPass = _config["Email:SmtpPass"]?.Trim();
        return string.Equals(smtpHost, "smtp.resend.com", StringComparison.OrdinalIgnoreCase)
            ? smtpPass
            : null;
    }

    private string? ResolveFromAddress()
    {
        var fromEmail = _config["Email:FromEmail"]?.Trim();
        if (string.IsNullOrWhiteSpace(fromEmail))
            return null;

        if (fromEmail.Contains('<') && fromEmail.Contains('>'))
            return fromEmail;

        var fromName = _config["Email:FromName"]?.Trim();
        return string.IsNullOrWhiteSpace(fromName)
            ? fromEmail
            : $"{fromName} <{fromEmail}>";
    }

    private string ResolveResendApiUrl()
    {
        var baseUrl = _config["Email:ResendBaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "https://api.resend.com";

        return $"{baseUrl.TrimEnd('/')}/emails";
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("CHANGEME", StringComparison.OrdinalIgnoreCase)
        || value.Contains("<", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractResendId(string responseBody)
    {
        try
        {
            using var json = JsonDocument.Parse(responseBody);
            return json.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractResendError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return string.Empty;

        try
        {
            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("message", out var message))
                return message.GetString() ?? string.Empty;
            if (json.RootElement.TryGetProperty("error", out var error))
                return error.ValueKind == JsonValueKind.String ? error.GetString() ?? string.Empty : error.ToString();
        }
        catch
        {
            // Keep the raw response below.
        }

        return responseBody.Length > 500 ? responseBody[..500] : responseBody;
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
