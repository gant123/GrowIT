using System.Collections;
using System.Reflection;
using GrowIT.Client.Layout;
using Microsoft.AspNetCore.Components;

namespace GrowIT.Backend.Tests;

/// <summary>
/// Guards the anonymous-access allowlist in Routes.razor. Every routable page that opts into
/// the public chrome (PublicLayout) must also be listed in the PublicPages set, or the Blazor
/// router treats it as authenticated-only and bounces anonymous visitors to /login. This exact
/// omission (ConfirmEmail missing) broke the email-confirmation link for every new user; the
/// API-level tests could not catch it because they never traverse the Blazor router.
/// </summary>
public class RoutePublicAccessTests
{
    [Fact]
    public void EveryPublicLayoutPage_IsListedInRoutesPublicPages()
    {
        var publicPages = GetPublicPages();

        // A page may declare more than one @page route, so use the plural attribute lookups
        // (the singular GetCustomAttribute<T>() throws AmbiguousMatchException on multi-route pages).
        var offenders = typeof(GrowIT.Client.Routes).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttributes<RouteAttribute>().Any())
            .Where(t => t.GetCustomAttributes<LayoutAttribute>().FirstOrDefault()?.LayoutType == typeof(PublicLayout))
            .Where(t => !publicPages.Contains(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"These PublicLayout pages are missing from Routes.razor PublicPages and will redirect anonymous visitors to /login: {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("Login")]
    [InlineData("Register")]
    [InlineData("ForgotPassword")]
    [InlineData("ResetPassword")]
    [InlineData("AcceptInvite")]
    [InlineData("ConfirmEmail")]
    public void CriticalAnonymousAuthPages_ArePublic(string pageName)
    {
        Assert.Contains(pageName, GetPublicPages());
    }

    private static HashSet<string> GetPublicPages()
    {
        var field = typeof(GrowIT.Client.Routes)
            .GetField("PublicPages", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var value = field!.GetValue(null);
        Assert.NotNull(value);

        // Copy into a case-sensitive comparer-agnostic set for assertion convenience.
        return new HashSet<string>(((IEnumerable)value!).Cast<string>(), StringComparer.Ordinal);
    }
}
