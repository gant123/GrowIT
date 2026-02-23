using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IFeedbackService
{
    Task<BetaFeedbackListItemDto?> SubmitAsync(CreateBetaFeedbackRequest request);
    Task<List<BetaFeedbackListItemDto>> GetMineAsync(BetaFeedbackQueryParams? query = null);
    Task<List<BetaFeedbackListItemDto>> GetAdminAsync(BetaFeedbackQueryParams? query = null);
    Task<BetaFeedbackListItemDto?> UpdateStatusAsync(Guid id, UpdateBetaFeedbackStatusRequest request);
}

public class FeedbackService : BaseApiService, IFeedbackService
{
    public FeedbackService(HttpClient http) : base(http) { }

    public Task<BetaFeedbackListItemDto?> SubmitAsync(CreateBetaFeedbackRequest request) =>
        PostAsync<CreateBetaFeedbackRequest, BetaFeedbackListItemDto>("api/feedback", request);

    public async Task<List<BetaFeedbackListItemDto>> GetMineAsync(BetaFeedbackQueryParams? query = null) =>
        await GetAsync<List<BetaFeedbackListItemDto>>($"api/feedback/mine{BuildQuery(query)}") ?? new();

    public async Task<List<BetaFeedbackListItemDto>> GetAdminAsync(BetaFeedbackQueryParams? query = null) =>
        await GetAsync<List<BetaFeedbackListItemDto>>($"api/admin/feedback{BuildQuery(query)}") ?? new();

    public Task<BetaFeedbackListItemDto?> UpdateStatusAsync(Guid id, UpdateBetaFeedbackStatusRequest request) =>
        PutAsync<UpdateBetaFeedbackStatusRequest, BetaFeedbackListItemDto>($"api/admin/feedback/{id}/status", request);

    private static string BuildQuery(BetaFeedbackQueryParams? query)
    {
        if (query is null) return string.Empty;
        var parts = new List<string>();
        Add(parts, "search", query.Search);
        Add(parts, "status", query.Status);
        Add(parts, "category", query.Category);
        Add(parts, "severity", query.Severity);
        if (query.Take.HasValue) Add(parts, "take", query.Take.Value.ToString());
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static void Add(List<string> parts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
    }
}
