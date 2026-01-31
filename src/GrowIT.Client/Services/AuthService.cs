using System.Net.Http.Json;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Components.Authorization;
using Blazored.LocalStorage;
using GrowIT.Client.Auth;

namespace GrowIT.Client.Services;

public interface IAuthService
{
    Task<AuthResponse?> Login(LoginRequest request);
    Task<AuthResponse?> Register(RegisterRequest request);
    Task Logout();
    Task<bool> ForgotPassword(ForgotPasswordRequest request);
    Task<bool> ResetPassword(ResetPasswordRequest request);
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
}

public class AuthService : BaseApiService, IAuthService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILocalStorageService _localStorage;

    public AuthService(HttpClient http, 
                       AuthenticationStateProvider authStateProvider, 
                       ILocalStorageService localStorage) : base(http)
    {
        _authStateProvider = authStateProvider;
        _localStorage = localStorage;
    }

    public async Task<AuthResponse?> Login(LoginRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", request, _jsonOptions);
        
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
            if (result != null && !string.IsNullOrEmpty(result.Token))
            {
                await _localStorage.SetItemAsync("authToken", result.Token);
                await ((CustomAuthStateProvider)_authStateProvider).MarkUserAsAuthenticated(request.Email);
                _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.Token);
                return result;
            }
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null; // Known failure case for UI to handle
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Login failed: {response.StatusCode}. {errorContent}");
        }
        
        return null;
    }

    public async Task<AuthResponse?> Register(RegisterRequest request)
    {
        // The API currently returns { Message, TenantId } 
        // We'll call the register endpoint and then log the user in.
        
        var response = await _http.PostAsJsonAsync("api/auth/register", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            // Auto login after registration
            return await Login(new LoginRequest { Email = request.Email, Password = request.Password });
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Registration failed: {response.StatusCode}. Details: {errorContent}");
        }
    }

    public async Task Logout()
    {
        await _localStorage.RemoveItemAsync("authToken");
        await ((CustomAuthStateProvider)_authStateProvider).MarkUserAsLoggedOut();
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<bool> ForgotPassword(ForgotPasswordRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/forgot-password", request, _jsonOptions);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ResetPassword(ResetPasswordRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/reset-password", request, _jsonOptions);
        return response.IsSuccessStatusCode;
    }
}
