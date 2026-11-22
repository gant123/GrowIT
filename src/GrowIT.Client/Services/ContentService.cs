using System.Net;
using System.Net.Http.Json;
using GrowIT.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace GrowIT.Client.Services;

public interface IContentService
{
    Task<List<PublicBlogPostDto>> GetPublishedBlogPostsAsync(int take = 24);

    /// <summary>A published article by slug, or null when it doesn't exist or isn't published.</summary>
    Task<PublicBlogPostDto?> GetPublishedBlogPostAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Whether any article is published (drives the public Blog nav link). Cached briefly and
    /// never throws: on failure the link is simply hidden.
    /// </summary>
    Task<bool> HasPublishedBlogPostsAsync();
    Task<ContactSubmissionReceivedDto?> SubmitContactSubmissionAsync(CreateContactSubmissionRequest request);

    Task<List<AdminBlogPostDto>> GetAdminBlogPostsAsync(bool includeDrafts = true);
    Task<AdminBlogPostDto?> CreateBlogPostAsync(CreateBlogPostRequest request);
    Task<AdminBlogPostDto?> UpdateBlogPostAsync(Guid id, UpdateBlogPostRequest request);
    Task DeleteBlogPostAsync(Guid id);

    Task<List<ContactSubmissionAdminDto>> GetContactSubmissionsAsync(ContactSubmissionQueryParams? query = null);
    Task<ContactSubmissionAdminDto?> UpdateContactSubmissionReviewAsync(Guid id, UpdateContactSubmissionReviewRequest request);
}

public class ContentService : BaseApiService, IContentService
{
    private const string HasPublishedPostsCacheKey = "growit:public-blog:has-published-posts";
    private readonly IMemoryCache _cache;

    public ContentService(HttpClient http, AppNotificationService notifications, IMemoryCache cache) : base(http, notifications)
    {
        _cache = cache;
    }

    public async Task<List<PublicBlogPostDto>> GetPublishedBlogPostsAsync(int take = 24) =>
        await GetAsync<List<PublicBlogPostDto>>($"api/content/blog?take={Math.Clamp(take, 1, 100)}") ?? new();

    public async Task<PublicBlogPostDto?> GetPublishedBlogPostAsync(string slug, CancellationToken ct = default)
    {
        var normalized = (slug ?? string.Empty).Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        using var response = await _http.GetAsync($"api/content/blog/{Uri.EscapeDataString(normalized)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessWithDetailsAsync(response);
        return await response.Content.ReadFromJsonAsync<PublicBlogPostDto>(_jsonOptions, ct);
    }

    public async Task<bool> HasPublishedBlogPostsAsync()
    {
        if (_cache.TryGetValue(HasPublishedPostsCacheKey, out bool cached))
        {
            return cached;
        }

        try
        {
            using var response = await _http.GetAsync("api/content/blog?take=1");
            if (!response.IsSuccessStatusCode)
            {
                _cache.Set(HasPublishedPostsCacheKey, false, TimeSpan.FromSeconds(30));
                return false;
            }

            var posts = await response.Content.ReadFromJsonAsync<List<PublicBlogPostDto>>(_jsonOptions);
            var hasPosts = posts is { Count: > 0 };
            _cache.Set(HasPublishedPostsCacheKey, hasPosts, TimeSpan.FromMinutes(2));
            return hasPosts;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _cache.Set(HasPublishedPostsCacheKey, false, TimeSpan.FromSeconds(30));
            return false;
        }
    }

    private void ForgetPublishedPostsState() => _cache.Remove(HasPublishedPostsCacheKey);

    public Task<ContactSubmissionReceivedDto?> SubmitContactSubmissionAsync(CreateContactSubmissionRequest request) =>
        PostAsync<CreateContactSubmissionRequest, ContactSubmissionReceivedDto>("api/content/contact", request);

    public async Task<List<AdminBlogPostDto>> GetAdminBlogPostsAsync(bool includeDrafts = true) =>
        await GetAsync<List<AdminBlogPostDto>>($"api/admin/content/blog?includeDrafts={includeDrafts.ToString().ToLowerInvariant()}") ?? new();

    public async Task<AdminBlogPostDto?> CreateBlogPostAsync(CreateBlogPostRequest request)
    {
        var created = await PostAsync<CreateBlogPostRequest, AdminBlogPostDto>("api/admin/content/blog", request);
        ForgetPublishedPostsState();
        return created;
    }

    public async Task<AdminBlogPostDto?> UpdateBlogPostAsync(Guid id, UpdateBlogPostRequest request)
    {
        var updated = await PutAsync<UpdateBlogPostRequest, AdminBlogPostDto>($"api/admin/content/blog/{id}", request);
        ForgetPublishedPostsState();
        return updated;
    }

    public async Task DeleteBlogPostAsync(Guid id)
    {
        await DeleteAsync($"api/admin/content/blog/{id}");
        ForgetPublishedPostsState();
    }

    public async Task<List<ContactSubmissionAdminDto>> GetContactSubmissionsAsync(ContactSubmissionQueryParams? query = null) =>
        await GetAsync<List<ContactSubmissionAdminDto>>($"api/admin/content/contact{BuildContactQuery(query)}") ?? new();

    public Task<ContactSubmissionAdminDto?> UpdateContactSubmissionReviewAsync(Guid id, UpdateContactSubmissionReviewRequest request) =>
        PutAsync<UpdateContactSubmissionReviewRequest, ContactSubmissionAdminDto>($"api/admin/content/contact/{id}/review", request);

    private static string BuildContactQuery(ContactSubmissionQueryParams? query)
    {
        if (query is null) return string.Empty;

        var parts = new List<string>();
        if (query.ReviewedOnly.HasValue)
        {
            parts.Add($"reviewedOnly={query.ReviewedOnly.Value.ToString().ToLowerInvariant()}");
        }

        if (query.Take.HasValue)
        {
            parts.Add($"take={Math.Clamp(query.Take.Value, 1, 1000)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
