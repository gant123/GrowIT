namespace GrowIT.Core.Interfaces;

public sealed class EmailSendResult
{
    public bool Succeeded { get; set; }
    public string DeliveryMode { get; set; } = "Unknown";
    public string Message { get; set; } = string.Empty;
    public string? FallbackFilePath { get; set; }
    public string? Error { get; set; }
}

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
    Task<EmailSendResult> SendEmailDetailedAsync(string to, string subject, string body);
}
