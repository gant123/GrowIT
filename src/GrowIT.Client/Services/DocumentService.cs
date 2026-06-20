using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IDocumentService
{
    Task<List<DocumentDto>> GetDocumentsAsync(Guid? clientId = null, CancellationToken ct = default);
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

    public async Task<Guid> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
    {
        var response = await PostAsync<CreateDocumentRequest, CreateResponse>(Endpoint, request, ct);
        return response?.DocumentId ?? Guid.Empty;
    }

    public Task UpdateDocumentAsync(Guid id, UpdateDocumentRequest request, CancellationToken ct = default) =>
        PutAsync($"{Endpoint}/{id}", request, ct);

    public Task DeleteDocumentAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Endpoint}/{id}", ct);

    private class CreateResponse
    {
        public Guid DocumentId { get; set; }
    }
}
