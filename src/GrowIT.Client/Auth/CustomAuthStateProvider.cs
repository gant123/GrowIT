using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace GrowIT.Client.Auth;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _http;

    public CustomAuthStateProvider(ILocalStorageService localStorage, HttpClient http)
    {
        _localStorage = localStorage;
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // 1. Try to get token from browser storage
        var token = await _localStorage.GetItemAsync<string>("authToken");

        // 2. If no token, return "Anonymous" (Not Logged In)
        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        // 3. If token exists, attach it to outgoing API requests
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 4. Decode the user info from the token
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(ParseClaimsFromJwt(token), "jwt")));
    }

// Change 'void' to 'Task'
    public Task MarkUserAsAuthenticated(string email)
    {
        var authenticatedUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, email)
        }, "apiauth"));
        
        var authState = Task.FromResult(new AuthenticationState(authenticatedUser));
        NotifyAuthenticationStateChanged(authState);

        return Task.CompletedTask;
    }

public Task MarkUserAsLoggedOut()
    {
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var authState = Task.FromResult(new AuthenticationState(anonymousUser));
        NotifyAuthenticationStateChanged(authState);
        
        return Task.CompletedTask; // <--- Return a completed task
    }
    // Helper to read the "eyJ..." string without a heavy library
    private IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var jsonBytes = ParseBase64WithoutPadding(payload);
        var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

        if (keyValuePairs == null) return Enumerable.Empty<Claim>();

        var claims = new List<Claim>();

        foreach (var kvp in keyValuePairs)
        {
            var key = kvp.Key;
            var value = kvp.Value.ToString();

            // Map standard JWT claim keys to ClaimTypes if necessary
            // In JWT, 'role' is often just 'role' or the full URI
            if (key == "role" || key == ClaimTypes.Role)
            {
                if (value?.StartsWith("[") == true)
                {
                    var roles = JsonSerializer.Deserialize<string[]>(value);
                    if (roles != null)
                    {
                        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
                    }
                }
                else
                {
                    claims.Add(new Claim(ClaimTypes.Role, value ?? string.Empty));
                }
            }
            else if (key == "unique_name" || key == "sub")
            {
                claims.Add(new Claim(ClaimTypes.Name, value ?? string.Empty));
            }
            else
            {
                claims.Add(new Claim(key, value ?? string.Empty));
            }
        }

        return claims;
    }

    private byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }
}