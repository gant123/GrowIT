using System.Net.Http.Json;
using System.Text.Json;
using GrowIT.Shared.DTOs;
using Microsoft.JSInterop;

namespace GrowIT.Client.Services;

public interface IAuthService
{
    Task<AuthResponseDto?> Login(LoginRequest request);
    Task<RegisterResponseDto?> Register(RegisterRequest request);
    Task Logout();
    Task<bool> ForgotPassword(ForgotPasswordRequest request);
    Task<bool> ResetPassword(ResetPasswordRequest request);
    Task<InviteValidationDto?> ValidateInvite(string token, string email);
    Task<AuthResponseDto?> AcceptInvite(AcceptInviteRequest request);
    Task<ConfirmEmailResultDto?> ConfirmEmail(string userId, string token);
    Task<bool> ResendConfirmationEmail(ResendConfirmationEmailRequest request);
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

        throw new ApiException(string.IsNullOrWhiteSpace(result.Body)
            ? $"Login failed ({result.Status})."
            : GetErrorMessage(result.Body), result.Status);
    }

    public async Task<RegisterResponseDto?> Register(RegisterRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/register", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<RegisterResponseDto>(_jsonOptions);
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(errorContent)
            ? $"Registration failed ({(int)response.StatusCode})."
            : GetErrorMessage(errorContent), (int)response.StatusCode);
    }

    public async Task Logout()
    {
        var result = await PostBrowserAuthAsync("bff/auth/logout", payload: null);
        if (!result.Ok && result.Status != 401)
        {
            throw new ApiException(string.IsNullOrWhiteSpace(result.Body)
                ? $"Logout failed ({result.Status})."
                : GetErrorMessage(result.Body), result.Status);
        }
    }

    public async Task<bool> ForgotPassword(ForgotPasswordRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/forgot-password", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Password reset request failed." : GetErrorMessage(body), (int)response.StatusCode);
    }

    public async Task<bool> ResetPassword(ResetPasswordRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/reset-password", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Password reset failed." : GetErrorMessage(body), (int)response.StatusCode);
    }

    public async Task<InviteValidationDto?> ValidateInvite(string token, string email)
    {
        var endpoint = $"api/auth/invites/validate?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        var response = await _http.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Invite validation failed." : GetErrorMessage(body), (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<InviteValidationDto>(_jsonOptions);
    }

    public async Task<AuthResponseDto?> AcceptInvite(AcceptInviteRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/accept-invite", request, _jsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Invite acceptance failed." : GetErrorMessage(body), (int)response.StatusCode);
        }

        await response.Content.ReadFromJsonAsync<AuthResponseDto>(_jsonOptions);

        return await Login(new LoginRequest
        {
            Email = request.Email,
            Password = request.Password
        });
    }

    public async Task<ConfirmEmailResultDto?> ConfirmEmail(string userId, string token)
    {
        var endpoint = $"api/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
        var response = await _http.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Email confirmation failed." : GetErrorMessage(body), (int)response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<ConfirmEmailResultDto>(_jsonOptions);
    }

    public async Task<bool> ResendConfirmationEmail(ResendConfirmationEmailRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/resend-confirmation", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Resending the confirmation email failed." : GetErrorMessage(body), (int)response.StatusCode);
    }

    private Task<BrowserAuthResult> PostBrowserAuthAsync(string relativeUrl, object? payload) =>
        _js.InvokeAsync<BrowserAuthResult>("blazorInterop.authPostJson", relativeUrl, payload).AsTask();

    private sealed class BrowserAuthResult
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public string? Body { get; set; }
    }

    private static string GetErrorMessage(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return trimmed;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString() ?? trimmed;
                }

                if (root.TryGetProperty("message", out var lowerMessage) && lowerMessage.ValueKind == JsonValueKind.String)
                {
                    return lowerMessage.GetString() ?? trimmed;
                }

                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString() ?? trimmed;
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}
