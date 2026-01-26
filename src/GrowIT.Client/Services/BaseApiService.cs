using System.Net.Http.Json;
using System.Text.Json;

namespace GrowIT.Client.Services;

/// <summary>
/// Base API service providing common HTTP operations.
/// All services inherit from this to communicate with GrowIT.API.
/// </summary>
public abstract class BaseApiService
{
    protected readonly HttpClient _http;
    protected readonly JsonSerializerOptions _jsonOptions;

    protected BaseApiService(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    protected async Task<T?> GetAsync<T>(string endpoint)
    {
        var response = await _http.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(endpoint, data, _jsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
    }

    protected async Task PostAsync<TRequest>(string endpoint, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(endpoint, data, _jsonOptions);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(endpoint, data, _jsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
    }

    protected async Task PutAsync<TRequest>(string endpoint, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(endpoint, data, _jsonOptions);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<TResponse?> PatchAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        var content = JsonContent.Create(data, options: _jsonOptions);
        var response = await _http.PatchAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
    }

    protected async Task DeleteAsync(string endpoint)
    {
        var response = await _http.DeleteAsync(endpoint);
        response.EnsureSuccessStatusCode();
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