using System.Net.Http.Headers;
using GrowIT.Backend.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GrowIT.Client.Auth;

/// <summary>
/// Projects the current cookie-authenticated user into a bearer token for internal /api calls.
/// This keeps the browser on HttpOnly cookies while existing controller endpoints remain bearer-protected.
/// </summary>
public sealed class ApiAuthorizationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public ApiAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _authenticationStateProvider = authenticationStateProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.RequestUri = NormalizeApiRequestUri(request.RequestUri);

        if (request.Headers.Authorization is null)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
                user = authState.User;
            }

            var isAuthenticated = user?.Identity?.IsAuthenticated == true;
            if (isAuthenticated)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
                var token = await tokenService.TryCreateTokenAsync(user!);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private Uri NormalizeApiRequestUri(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return ResolveHttpBaseAddress();
        }

        if (!string.Equals(requestUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            return requestUri;
        }

        var baseAddress = ResolveHttpBaseAddress();
        var pathAndQuery = requestUri.PathAndQuery;
        if (string.IsNullOrWhiteSpace(pathAndQuery))
        {
            pathAndQuery = "/";
        }

        return new Uri(baseAddress, pathAndQuery.TrimStart('/'));
    }

    private Uri ResolveHttpBaseAddress()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            return new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}{httpContext.Request.PathBase}/");
        }

        var clientUrl = _configuration["ClientUrl"]
            ?? (_environment.IsDevelopment() ? "http://localhost:5245/" : "http://localhost/");

        return new Uri(clientUrl.EndsWith('/') ? clientUrl : $"{clientUrl}/");
    }
}
