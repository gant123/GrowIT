using GrowIT.Shared.DTOs;
using System.Net.Http.Headers;

namespace GrowIT.Client.Services;

public interface IReportService
{
    Task<List<RecentReport>> GetRecentReportsAsync(RecentReportsQueryParams? query = null);
    Task<List<ScheduledReport>> GetScheduledReportsAsync(ScheduledReportsQueryParams? query = null);
    Task<RecentReport?> GenerateReportAsync(GenerateReportRequest request);
    Task<ReportRunDetailDto?> GetReportRunDetailsAsync(Guid id);
    Task<DownloadedFile> DownloadReportAsync(Guid id);
    Task<ScheduledReport?> CreateScheduledReportAsync(CreateScheduledReportRequest request);
    Task<ScheduledReport?> UpdateScheduledReportAsync(Guid id, UpdateScheduledReportRequest request);
    Task DeleteScheduledReportAsync(Guid id);
}

public sealed record DownloadedFile(string FileName, string ContentType, byte[] Content);

public class ReportService : BaseApiService, IReportService
{
    private const string BaseEndpoint = "api/reports";

    public ReportService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<RecentReport>> GetRecentReportsAsync(RecentReportsQueryParams? query = null)
    {
        var url = $"{BaseEndpoint}/recent{BuildRecentQuery(query)}";
        return await GetAsync<List<RecentReport>>(url)
            ?? new List<RecentReport>();
    }

    public async Task<List<ScheduledReport>> GetScheduledReportsAsync(ScheduledReportsQueryParams? query = null)
    {
        var url = $"{BaseEndpoint}/scheduled{BuildScheduledQuery(query)}";
        return await GetAsync<List<ScheduledReport>>(url)
            ?? new List<ScheduledReport>();
    }

    public async Task<RecentReport?> GenerateReportAsync(GenerateReportRequest request)
    {
        return await PostAsync<GenerateReportRequest, RecentReport>($"{BaseEndpoint}/generate", request);
    }

    public async Task<ReportRunDetailDto?> GetReportRunDetailsAsync(Guid id)
    {
        return await GetAsync<ReportRunDetailDto>($"{BaseEndpoint}/{id}");
    }

    public async Task<DownloadedFile> DownloadReportAsync(Guid id)
    {
        using var response = await _http.GetAsync($"{BaseEndpoint}/{id}/download");
        await EnsureSuccessWithDetailsAsync(response);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = ResolveFileName(response.Content.Headers.ContentDisposition, contentType, id);

        return new DownloadedFile(fileName, contentType, bytes);
    }

    public async Task<ScheduledReport?> CreateScheduledReportAsync(CreateScheduledReportRequest request)
    {
        return await PostAsync<CreateScheduledReportRequest, ScheduledReport>($"{BaseEndpoint}/scheduled", request);
    }

    public async Task<ScheduledReport?> UpdateScheduledReportAsync(Guid id, UpdateScheduledReportRequest request)
    {
        return await PutAsync<UpdateScheduledReportRequest, ScheduledReport>($"{BaseEndpoint}/scheduled/{id}", request);
    }

    public async Task DeleteScheduledReportAsync(Guid id)
    {
        await DeleteAsync($"{BaseEndpoint}/scheduled/{id}");
    }

    private static string BuildRecentQuery(RecentReportsQueryParams? query)
    {
        if (query is null) return string.Empty;
        var parts = new List<string>();
        Add(parts, "search", query.Search);
        Add(parts, "reportType", query.ReportType);
        Add(parts, "format", query.Format);
        Add(parts, "status", query.Status);
        Add(parts, "dateFrom", query.DateFrom?.ToString("O"));
        Add(parts, "dateTo", query.DateTo?.ToString("O"));
        if (query.Take.HasValue) Add(parts, "take", query.Take.Value.ToString());
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string BuildScheduledQuery(ScheduledReportsQueryParams? query)
    {
        if (query is null) return string.Empty;
        var parts = new List<string>();
        Add(parts, "search", query.Search);
        Add(parts, "frequency", query.Frequency);
        if (query.IncludeInactive) Add(parts, "includeInactive", "true");
        if (query.Take.HasValue) Add(parts, "take", query.Take.Value.ToString());
        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static void Add(List<string> parts, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
    }

    private static string ResolveFileName(ContentDispositionHeaderValue? contentDisposition, string contentType, Guid id)
    {
        var raw = contentDisposition?.FileNameStar ?? contentDisposition?.FileName;
        if (string.IsNullOrWhiteSpace(raw))
        {
            var ext = contentType.ToLowerInvariant() switch
            {
                var t when t.Contains("spreadsheetml") => "xlsx",
                var t when t.Contains("ms-excel") => "xls",
                var t when t.Contains("csv") => "csv",
                var t when t.Contains("pdf") => "pdf",
                _ => "bin"
            };
            return $"growit-report-{id}.{ext}";
        }

        return raw.Trim().Trim('"');
    }
}
