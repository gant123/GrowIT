using System.Net.Http.Json;
using System.Text.Json;
using GrowIT.Shared.DTOs;
using Microsoft.JSInterop;

namespace GrowIT.Client.Services;

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed,
    LockedOut,
    Failed
}

/// <summary>
/// Structured sign-in outcome so pages can react to the account state
/// (unconfirmed email, lockout, ...) instead of matching on message text.
/// </summary>
public sealed record LoginResult(LoginStatus Status, string? Message = null)
{
    public bool Succeeded => Status == LoginStatus.Success;
}

public interface IAuthService
{
    Task<LoginResult> Login(LoginRequest request);
    Task<RegisterResponseDto?> Register(RegisterRequest request);
    Task Logout();
    Task<bool> ForgotPassword(ForgotPasswordRequest request);
    Task<bool> ResetPassword(ResetPasswordRequest request);
    Task<InviteValidationDto?> ValidateInvite(string token, string email);
    Task<LoginResult> AcceptInvite(AcceptInviteRequest request);
    Task<ConfirmEmailResultDto?> ConfirmEmail(string userId, string token);
    Task<MessageResponse?> ResendConfirmationEmail(ResendConfirmationEmailRequest request);
}

public class AuthService : BaseApiService, IAuthService
{
    private readonly IJSRuntime _js;

    public AuthService(HttpClient http, AppNotificationService notifications, IJSRuntime js) : base(http, notifications)
    {
        _js = js;
    }

    public async Task<LoginResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new LoginResult(LoginStatus.InvalidCredentials, "Enter both your email and password.");
        }

        var result = await PostBrowserAuthAsync("bff/auth/login", request);
        if (result.Ok)
        {
            return new LoginResult(LoginStatus.Success);
        }

        var message = string.IsNullOrWhiteSpace(result.Body) ? null : GetErrorMessage(result.Body);
        return result.Status switch
        {
            401 => new LoginResult(LoginStatus.InvalidCredentials,
                "Invalid email or password. Please try again."),
            423 => new LoginResult(LoginStatus.LockedOut,
                message ?? "This account is temporarily locked. Wait about 15 minutes and try again, or reset your password."),
            403 when HasErrorCode(result.Body, "email-not-confirmed") => new LoginResult(LoginStatus.EmailNotConfirmed,
                message ?? "Your email address has not been confirmed yet."),
            _ => new LoginResult(LoginStatus.Failed,
                message ?? $"We couldn't sign you in ({result.Status}). Refresh the page and try again.")
        };
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

    public async Task<LoginResult> AcceptInvite(AcceptInviteRequest request)
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

    public async Task<MessageResponse?> ResendConfirmationEmail(ResendConfirmationEmailRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/auth/resend-confirmation", request, _jsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<MessageResponse>(_jsonOptions);
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(body) ? "Resending the confirmation email failed." : GetErrorMessage(body), (int)response.StatusCode);
    }

    private Task<BrowserAuthResult> PostBrowserAuthAsync(string relativeUrl, object? payload) =>
        _js.InvokeAsync<BrowserAuthResult>("blazorInterop.authPostJson", relativeUrl, payload).AsTask();

    private static string? ExtractErrorList(JsonElement root)
    {
        foreach (var propertyName in new[] { "Errors", "errors" })
        {
            if (root.TryGetProperty(propertyName, out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
        }

        return null;
    }

    private static string? CombineMessage(string? message, string? errorDetails)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return errorDetails;
        }

        return string.IsNullOrWhiteSpace(errorDetails) ? message : $"{message} {errorDetails}";
    }

    private static bool HasErrorCode(string? body, string code)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body.Trim());
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("code", out var value)
                && value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), code, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class BrowserAuthResult
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public string? Body { get; set; }
    }

    private static string GetErrorMessage(string body)
    {
        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[') && !trimmed.StartsWith('"'))
        {
            return trimmed;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Bare-string responses (e.g. 423 lockout messages) serialize as a JSON string;
            // unwrap so the UI doesn't show surrounding quote marks.
            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? trimmed;
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var messages = root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                // Validation responses carry the reasons in an "errors" array — surface
                // them, otherwise the user just sees "Registration failed." with no clue why.
                var errorDetails = ExtractErrorList(root);

                if (root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return CombineMessage(message.GetString(), errorDetails) ?? trimmed;
                }

                if (root.TryGetProperty("message", out var lowerMessage) && lowerMessage.ValueKind == JsonValueKind.String)
                {
                    return CombineMessage(lowerMessage.GetString(), errorDetails) ?? trimmed;
                }

                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return CombineMessage(detail.GetString(), errorDetails) ?? trimmed;
                }

                if (!string.IsNullOrWhiteSpace(errorDetails))
                {
                    return errorDetails;
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}
