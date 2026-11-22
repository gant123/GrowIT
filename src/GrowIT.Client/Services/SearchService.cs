using System.Net.Http.Json;
using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface ISearchService
{
    /// <summary>
    /// Searches people, household members, programs and money sources in the current
    /// organization. Returns an empty list for short queries or on failure (no error toast:
    /// the top-bar search falls back to page matches).
    /// </summary>
    Task<List<SearchResultDto>> SearchAsync(string query, CancellationToken ct = default);
}

public class SearchService : BaseApiService, ISearchService
{
    public SearchService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<SearchResultDto>> SearchAsync(string query, CancellationToken ct = default)
    {
        var term = (query ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return [];
        }

        try
        {
            using var response = await _http.GetAsync($"api/search?q={Uri.EscapeDataString(term)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<SearchResultDto>>(_jsonOptions, ct) ?? [];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Never break the app shell over a failed search; page shortcuts still work.
            return [];
        }
    }
}
