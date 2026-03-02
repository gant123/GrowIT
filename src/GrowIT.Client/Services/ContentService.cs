using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IContentService
{
    Task<List<PublicBlogPostDto>> GetPublishedBlogPostsAsync(int take = 24);
    Task<ContactSubmissionReceivedDto?> SubmitContactSubmissionAsync(CreateContactSubmissionRequest request);

    Task<List<AdminBlogPostDto>> GetAdminBlogPostsAsync(bool includeDrafts = true);
    Task<AdminBlogPostDto?> CreateBlogPostAsync(CreateBlogPostRequest request);
    Task<AdminBlogPostDto?> UpdateBlogPostAsync(Guid id, UpdateBlogPostRequest request);
    Task DeleteBlogPostAsync(Guid id);

    Task<List<ContactSubmissionAdminDto>> GetContactSubmissionsAsync(ContactSubmissionQueryParams? query = null);
    Task<ContactSubmissionAdminDto?> UpdateContactSubmissionReviewAsync(Guid id, UpdateContactSubmissionReviewRequest request);
    Task<List<SecurityAccessAttemptDto>> GetSecurityAccessAttemptsAsync(SecurityAccessAttemptQueryParams? query = null);
}

public class ContentService : BaseApiService, IContentService
{
    public ContentService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<PublicBlogPostDto>> GetPublishedBlogPostsAsync(int take = 24) =>
        await GetAsync<List<PublicBlogPostDto>>($"api/content/blog?take={Math.Clamp(take, 1, 100)}") ?? new();

    public Task<ContactSubmissionReceivedDto?> SubmitContactSubmissionAsync(CreateContactSubmissionRequest request) =>
        PostAsync<CreateContactSubmissionRequest, ContactSubmissionReceivedDto>("api/content/contact", request);

    public async Task<List<AdminBlogPostDto>> GetAdminBlogPostsAsync(bool includeDrafts = true) =>
        await GetAsync<List<AdminBlogPostDto>>($"api/admin/content/blog?includeDrafts={includeDrafts.ToString().ToLowerInvariant()}") ?? new();

    public Task<AdminBlogPostDto?> CreateBlogPostAsync(CreateBlogPostRequest request) =>
        PostAsync<CreateBlogPostRequest, AdminBlogPostDto>("api/admin/content/blog", request);

    public Task<AdminBlogPostDto?> UpdateBlogPostAsync(Guid id, UpdateBlogPostRequest request) =>
        PutAsync<UpdateBlogPostRequest, AdminBlogPostDto>($"api/admin/content/blog/{id}", request);

    public Task DeleteBlogPostAsync(Guid id) =>
        DeleteAsync($"api/admin/content/blog/{id}");

    public async Task<List<ContactSubmissionAdminDto>> GetContactSubmissionsAsync(ContactSubmissionQueryParams? query = null) =>
        await GetAsync<List<ContactSubmissionAdminDto>>($"api/admin/content/contact{BuildContactQuery(query)}") ?? new();

    public Task<ContactSubmissionAdminDto?> UpdateContactSubmissionReviewAsync(Guid id, UpdateContactSubmissionReviewRequest request) =>
        PutAsync<UpdateContactSubmissionReviewRequest, ContactSubmissionAdminDto>($"api/admin/content/contact/{id}/review", request);

    public async Task<List<SecurityAccessAttemptDto>> GetSecurityAccessAttemptsAsync(SecurityAccessAttemptQueryParams? query = null) =>
        await GetAsync<List<SecurityAccessAttemptDto>>($"api/admin/content/security-attempts{BuildSecurityQuery(query)}") ?? new();

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

    private static string BuildSecurityQuery(SecurityAccessAttemptQueryParams? query)
    {
        if (query is null) return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Ip))
        {
            parts.Add($"ip={Uri.EscapeDataString(query.Ip.Trim())}");
        }

        if (query.Take.HasValue)
        {
            parts.Add($"take={Math.Clamp(query.Take.Value, 1, 1000)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
