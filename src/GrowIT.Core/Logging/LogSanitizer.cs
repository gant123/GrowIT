namespace GrowIT.Core.Logging;

/// <summary>
/// Helpers for keeping personally identifiable information (PII) out of logs while
/// preserving enough signal to debug delivery and auth flows. Prefer logging a userId
/// as the correlation key when one is available; use these helpers when only the raw
/// email is in scope (e.g. the email-delivery layer).
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Masks an email address for logging. Keeps the first character of the local part
    /// and the full domain, replacing the rest of the local part with asterisks — enough
    /// to tell recipients apart and spot domain-wide delivery issues without recording the
    /// full address. Single-character local parts are masked entirely.
    /// Examples: "demontegant@gmail.com" =&gt; "d***@gmail.com", "a@x.io" =&gt; "*@x.io",
    /// null/blank =&gt; "(none)", malformed =&gt; "(invalid-email)".
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "(none)";

        var trimmed = email.Trim();
        var atIndex = trimmed.IndexOf('@');

        // No local part ('@...') or no '@' at all: never echo the raw value.
        if (atIndex <= 0)
            return "(invalid-email)";

        var local = trimmed[..atIndex];
        var domain = trimmed[(atIndex + 1)..];

        return local.Length == 1
            ? $"*@{domain}"
            : $"{local[0]}***@{domain}";
    }
}
