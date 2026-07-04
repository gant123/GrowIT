using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IDocumentService
{
    Task<List<DocumentDto>> GetDocumentsAsync(Guid? clientId = null, CancellationToken ct = default);
    Task<List<DocumentDto>> GetDocumentsAsync(DocumentQueryParams query, CancellationToken ct = default);
    Task<Guid> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default);
    Task UpdateDocumentAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
}

public class DocumentService : BaseApiService, IDocumentService
{
    private const string Endpoint = "api/documents";

    public DocumentService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<DocumentDto>> GetDocumentsAsync(Guid? clientId = null, CancellationToken ct = default)
    {
        var suffix = clientId.HasValue ? $"?clientId={clientId}" : string.Empty;
        return await GetAsync<List<DocumentDto>>($"{Endpoint}{suffix}", ct) ?? [];
    }

    public async Task<List<DocumentDto>> GetDocumentsAsync(DocumentQueryParams query, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (query.ClientId.HasValue)
        {
            parts.Add($"clientId={query.ClientId}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parts.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        parts.Add($"pageNumber={query.PageNumber}");
        parts.Add($"pageSize={query.PageSize}");

        var suffix = parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        return await GetAsync<List<DocumentDto>>($"{Endpoint}{suffix}", ct) ?? [];
    }

    public async Task<Guid> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        var response = await PostAsync<CreateDocumentRequest, EntityCreatedResponse>(Endpoint, request, ct);
        return response?.DocumentId ?? Guid.Empty;
    }

    public Task UpdateDocumentAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct = default) =>
        PutAsync($"{Endpoint}/{id}", request, ct);

    public Task DeleteDocumentAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Endpoint}/{id}", ct);
}
