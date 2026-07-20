using System.Text.RegularExpressions;
using GrowIT.Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GrowIT.Backend.Tests;

/// <summary>
/// Guards the Content-Security-Policy hardening (ApplySecurityHeaders in Program.cs): script-src
/// must use a per-request nonce and never 'unsafe-inline'. CSP is only enforced when
/// !IsDevelopment(), so these tests boot the host in the Production environment.
/// </summary>
public class SecurityHeadersTests
{
    private static GrowItApiFactory ProductionFactory() => new(environment: "Production");

    private static HttpClient AnonymousClient(GrowItApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task Csp_ScriptSrc_UsesNonce_AndNotUnsafeInline_WhenEnforced()
    {
        using var factory = ProductionFactory();
        using var client = AnonymousClient(factory);

        var response = await client.GetAsync("/healthz");

        Assert.True(
            response.Headers.TryGetValues("Content-Security-Policy", out var values),
            "CSP header should be present in non-Development environments.");
        var csp = string.Join(" ", values!);

        var scriptSrc = GetDirective(csp, "script-src");
        Assert.Contains("'self'", scriptSrc);
        Assert.Contains("'nonce-", scriptSrc);
        Assert.DoesNotContain("'unsafe-inline'", scriptSrc);

        // style-src intentionally KEEPS 'unsafe-inline' (component libraries inject inline styles
        // that can't be nonced). This documents that asymmetry so it isn't "fixed" by mistake.
        var styleSrc = GetDirective(csp, "style-src");
        Assert.Contains("'unsafe-inline'", styleSrc);
    }

    [Fact]
    public async Task Csp_HeaderNonce_Matches_InlineScriptTag()
    {
        using var factory = ProductionFactory();
        using var client = AnonymousClient(factory);

        // /login renders the full document (App.razor head) and does not redirect on GET.
        var response = await client.GetAsync("/login");
        response.EnsureSuccessStatusCode();

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var values));
        var scriptSrc = GetDirective(string.Join(" ", values!), "script-src");

        var headerNonce = Regex.Match(scriptSrc, @"'nonce-(?<n>[^']+)'").Groups["n"].Value;
        Assert.False(string.IsNullOrEmpty(headerNonce), "script-src should carry a nonce.");

        var body = await response.Content.ReadAsStringAsync();
        var tagNonce = Regex.Match(body, @"<script[^>]*\bnonce=""(?<n>[^""]+)""").Groups["n"].Value;
        Assert.False(string.IsNullOrEmpty(tagNonce), "The inline bootstrap <script> should carry a nonce.");

        Assert.Equal(headerNonce, tagNonce);
    }

    [Fact]
    public async Task Csp_IsNotEnforced_InDevelopment()
    {
        // Default factory runs in Development, where the CSP is deliberately not applied
        // (it would break Browser Link / hot reload). This pins that behavior.
        using var factory = new GrowItApiFactory();
        using var client = AnonymousClient(factory);

        var response = await client.GetAsync("/healthz");

        Assert.False(
            response.Headers.Contains("Content-Security-Policy"),
            "CSP should not be enforced in the Development environment.");
    }

    private static string GetDirective(string csp, string name)
    {
        foreach (var part in csp.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals(name, StringComparison.Ordinal) ||
                part.StartsWith(name + " ", StringComparison.Ordinal))
            {
                return part;
            }
        }

        return string.Empty;
    }
}
