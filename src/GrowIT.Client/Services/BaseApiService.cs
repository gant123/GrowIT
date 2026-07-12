using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GrowIT.Client.Services;

/// <summary>
/// Base API service providing common HTTP operations.
/// All services inherit from this to communicate with the backend host.
/// </summary>
public abstract class BaseApiService
{
    protected readonly HttpClient _http;
    protected readonly JsonSerializerOptions _jsonOptions;
    protected readonly AppNotificationService _notifications;

    protected BaseApiService(HttpClient http, AppNotificationService notifications)
    {
        _http = http;
        _notifications = notifications;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    protected async Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.GetAsync(endpoint, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, ct);
    }

    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.PostAsJsonAsync(endpoint, data, _jsonOptions, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, ct);
    }

    protected async Task PostAsync<TRequest>(string endpoint, TRequest data, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.PostAsJsonAsync(endpoint, data, _jsonOptions, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
    }

    protected async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.PutAsJsonAsync(endpoint, data, _jsonOptions, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, ct);
    }

    protected async Task PutAsync<TRequest>(string endpoint, TRequest data, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.PutAsJsonAsync(endpoint, data, _jsonOptions, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
    }

    protected async Task<TResponse?> PatchAsync<TRequest, TResponse>(string endpoint, TRequest data, CancellationToken ct = default)
    {
        var content = JsonContent.Create(data, options: _jsonOptions);
        var response = await SendAsync(() => _http.PatchAsync(endpoint, content, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, ct);
    }

    protected async Task DeleteAsync(string endpoint, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.DeleteAsync(endpoint, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
    }

    protected async Task<TResponse?> DeleteAsync<TResponse>(string endpoint, CancellationToken ct = default)
    {
        var response = await SendAsync(() => _http.DeleteAsync(endpoint, ct), ct);
        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, ct);
    }

    protected async Task EnsureSuccessWithDetailsAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = response.Content is null
            ? string.Empty
            : (await response.Content.ReadAsStringAsync()).Trim();

        var message = BuildErrorMessage(response.StatusCode, body, response.ReasonPhrase);
        NotifyFailure(response.StatusCode, message);

        throw new ApiException(message, (int)response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> action, CancellationToken ct = default)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException ex)
        {
            const string message = "We couldn't reach the server. Check your connection and try again.";
            _notifications.Error(message);
            throw new ApiException(message, ex);
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-initiated cancellation (e.g. navigating away) — not a timeout, no toast.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            const string message = "The request took too long. Please try again.";
            _notifications.Warning(message);
            throw new ApiException(message, ex);
        }
    }

    private void NotifyFailure(HttpStatusCode statusCode, string message)
    {
        switch (statusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.TooManyRequests:
                _notifications.Warning(message);
                break;
            default:
                _notifications.Error(message);
                break;
        }
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string body, string? reasonPhrase)
    {
        var detail = ExtractProblemDetail(body);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return detail!;
        }

        return statusCode switch
        {
            HttpStatusCode.BadRequest => "We couldn't process that request. Check the submitted values and try again.",
            HttpStatusCode.Unauthorized => "Your session is missing or expired. Sign in and try again.",
            HttpStatusCode.Forbidden => "You do not have permission to perform that action.",
            HttpStatusCode.NotFound => "The requested resource could not be found.",
            HttpStatusCode.Conflict => "That action could not be completed because the data changed.",
            HttpStatusCode.TooManyRequests => "Too many requests were sent. Wait a moment and try again.",
            _ when (int)statusCode >= 500 => "A server error occurred. Please try again in a moment.",
            _ => $"API request failed with status {(int)statusCode} ({reasonPhrase})."
        };
    }

    private static string? ExtractProblemDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[') && !trimmed.StartsWith('"'))
        {
            return trimmed;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Endpoints that return a bare string (e.g. BadRequest("...")) serialize as a
            // JSON string; unwrap it so the user doesn't see surrounding quote marks.
            if (root.ValueKind == JsonValueKind.String)
            {
                var text = root.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            // Validation endpoints may return an array of error strings; join them.
            if (root.ValueKind == JsonValueKind.Array)
            {
                var messages = root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                return messages.Count > 0 ? string.Join(" ", messages) : null;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("detail", out var detail) &&
                    detail.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(detail.GetString()))
                {
                    return detail.GetString();
                }

                if (root.TryGetProperty("message", out var message) &&
                    message.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(message.GetString()))
                {
                    return message.GetString();
                }

                if (root.TryGetProperty("title", out var title) &&
                    title.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(title.GetString()))
                {
                    return title.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return trimmed;
    }
}

/// <summary>
/// Custom exception for API errors.
/// </summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    
    public ApiException(string message) : base(message) { }
    public ApiException(string message, int statusCode) : base(message) => StatusCode = statusCode;
    public ApiException(string message, Exception inner) : base(message, inner) { }
}
