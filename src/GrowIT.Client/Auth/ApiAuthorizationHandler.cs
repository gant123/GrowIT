using System.Net.Http.Headers;
using GrowIT.Backend.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace GrowIT.Client.Auth;

/// <summary>
/// Projects the current cookie-authenticated user into a bearer token for internal /api calls.
/// This keeps the browser on HttpOnly cookies while existing controller endpoints remain bearer-protected.
/// </summary>
public sealed class ApiAuthorizationHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly TokenService _tokenService;

    public ApiAuthorizationHandler(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authStateProvider,
        TokenService tokenService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authStateProvider = authStateProvider;
        _tokenService = tokenService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync();
                user = authState.User;
            }

            var isAuthenticated = user?.Identity?.IsAuthenticated == true;
            if (isAuthenticated)
            {
                var token = _tokenService.TryCreateToken(user!);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
