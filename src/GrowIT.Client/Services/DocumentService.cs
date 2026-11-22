using System.Net.Http.Headers;
using System.Net.Http.Json;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Components.Forms;

namespace GrowIT.Client.Services;

public interface IDocumentService
{
    Task<List<DocumentDto>> GetDocumentsAsync(Guid? clientId = null, CancellationToken ct = default);
    Task<List<DocumentDto>> GetDocumentsAsync(DocumentQueryParams query, CancellationToken ct = default);
    Task<Guid> UploadDocumentAsync(IBrowserFile file, UploadDocumentRequest request, CancellationToken ct = default);
    Task<DownloadedFile> DownloadDocumentAsync(Guid id, CancellationToken ct = default);
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

        if (query.Scope != DocumentScope.All)
        {
            parts.Add($"scope={query.Scope}");
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

    public async Task<Guid> UploadDocumentAsync(IBrowserFile file, UploadDocumentRequest request, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(DocumentUploadRules.MaxFileBytes, ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(file.Name) ? "document" : file.Name);

        if (request.ClientId.HasValue && request.ClientId.Value != Guid.Empty)
        {
            form.Add(new StringContent(request.ClientId.Value.ToString()), nameof(UploadDocumentRequest.ClientId));
        }

        form.Add(new StringContent(request.Category.ToString()), nameof(UploadDocumentRequest.Category));

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            form.Add(new StringContent(request.Title.Trim()), nameof(UploadDocumentRequest.Title));
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"{Endpoint}/upload", form, ct);
        }
        catch (HttpRequestException ex)
        {
            const string message = "We couldn't reach the server. Check your connection and try again.";
            _notifications.Error(message);
            throw new ApiException(message, ex);
        }

        using (response)
        {
            await EnsureSuccessWithDetailsAsync(response);
            var payload = await response.Content.ReadFromJsonAsync<EntityCreatedResponse>(_jsonOptions, ct);
            return payload?.DocumentId ?? Guid.Empty;
        }
    }

    public async Task<DownloadedFile> DownloadDocumentAsync(Guid id, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"{Endpoint}/{id}/download", HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            const string message = "We couldn't reach the server. Check your connection and try again.";
            _notifications.Error(message);
            throw new ApiException(message, ex);
        }

        using (response)
        {
            await EnsureSuccessWithDetailsAsync(response);

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var disposition = response.Content.Headers.ContentDisposition;
            var fileName = (disposition?.FileNameStar ?? disposition?.FileName)?.Trim('"');

            return new DownloadedFile(string.IsNullOrWhiteSpace(fileName) ? "document" : fileName, contentType, bytes);
        }
    }

    public Task DeleteDocumentAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Endpoint}/{id}", ct);
}
