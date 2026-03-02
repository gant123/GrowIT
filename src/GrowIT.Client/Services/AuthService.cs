using System.Net.Http.Json;
using GrowIT.Shared.DTOs;
using Microsoft.JSInterop;

namespace GrowIT.Client.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> Login(LoginRequest request);
    Task<AuthResponseDto?> Register(RegisterRequest request);
    Task Logout();
    Task<bool> ForgotPassword(ForgotPasswordRequest request);
    Task<bool> ResetPassword(ResetPasswordRequest request);
    Task<InviteValidationDto?> ValidateInvite(string token, string email);
    Task<AuthResponseDto?> AcceptInvite(AcceptInviteRequest request);
}

public class AuthService : BaseApiService, IAuthService
{
    private readonly IJSRuntime _js;

    public AuthService(HttpClient http, AppNotificationService notifications, IJSRuntime js) : base(http, notifications)
    {
        _js = js;
    }

    public async Task<AuthResponseDto?> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return null;
        }

        var result = await PostBrowserAuthAsync("bff/auth/login", request);
        if (result.Ok)
        {
            return new AuthResponseDto();
        }

        if (result.Status == 401)
        {
            return null;
        }

        throw new Exception(string.IsNullOrWhiteSpace(result.Body)
            ? $"Login failed ({result.Status})."
            : result.Body);
    }

    public async Task<AuthResponseDto?> Register(RegisterRequest request)
    {
        // The API currently returns { Message, TenantId } 
        // We'll call the register endpoint and then log the user in.
        
        var response = await _http.PostAsJsonAsync("api/auth/register", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            // Auto login after registration
            return await Login(new LoginRequest
            {
                Email = request.Email,
                Password = request.Password
            });
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Registration failed: {response.StatusCode}. Details: {errorContent}");
        }
    }

    public async Task Logout()
    {
        var result = await PostBrowserAuthAsync("bff/auth/logout", payload: null);
        if (!result.Ok && result.Status != 401)
        {
            throw new Exception(string.IsNullOrWhiteSpace(result.Body)
                ? $"Logout failed ({result.Status})."
                : result.Body);
        }
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

    public async Task<InviteValidationDto?> ValidateInvite(string token, string email)
    {
        var endpoint = $"api/auth/invites/validate?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        var response = await _http.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(body) ? "Invite validation failed." : body);
        }

        return await response.Content.ReadFromJsonAsync<InviteValidationDto>(_jsonOptions);
    }

    public async Task<AuthResponseDto?> AcceptInvite(AcceptInviteRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/accept-invite", request, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Exception(string.IsNullOrWhiteSpace(body) ? "Invite acceptance failed." : body);
        }

        await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);

        return await Login(new LoginRequest
        {
            Email = request.Email,
            Password = request.Password
        });
    }

    private Task<BrowserAuthResult> PostBrowserAuthAsync(string relativeUrl, object? payload) =>
        _js.InvokeAsync<BrowserAuthResult>("blazorInterop.authPostJson", relativeUrl, payload).AsTask();

    private sealed class BrowserAuthResult
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public string? Body { get; set; }
    }
}
