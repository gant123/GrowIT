using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace GrowIT.Client;

/// <summary>
/// Per-request Content-Security-Policy nonce.
/// <para>
/// A fresh value is generated once per request by the security-headers middleware in
/// <c>Program.cs</c> and stashed on <see cref="HttpContext.Items"/>. It is then emitted in two
/// places for the same response: the <c>script-src</c> directive of the CSP header
/// (<c>ApplySecurityHeaders</c>) and the <c>nonce</c> attribute of the single inline bootstrap
/// <c>&lt;script&gt;</c> in <c>App.razor</c>. Sharing one value lets us drop <c>'unsafe-inline'</c>
/// from <c>script-src</c> while still allowing our own trusted inline script to run.
/// </para>
/// </summary>
public static class CspNonce
{
    /// <summary>Key under which the request's nonce is stored on <see cref="HttpContext.Items"/>.</summary>
    public const string ItemsKey = "CspNonce";

    /// <summary>Generates a cryptographically-random, base64-encoded nonce (128 bits of entropy).</summary>
    public static string Create() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>
    /// Returns the nonce generated for the current request, or <c>null</c> if none was set
    /// (e.g. outside the request pipeline). Callers render an empty attribute in that case, which
    /// is harmless because a CSP nonce is only enforced when a matching header is present.
    /// </summary>
    public static string? Current(HttpContext? context)
        => context is not null && context.Items.TryGetValue(ItemsKey, out var value)
            ? value as string
            : null;
}
