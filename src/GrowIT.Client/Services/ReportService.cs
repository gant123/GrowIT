using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IReportService
{
    Task<List<RecentReport>> GetRecentReportsAsync();
    Task<List<ScheduledReport>> GetScheduledReportsAsync();
    Task<RecentReport?> GenerateReportAsync(GenerateReportRequest request);
    Task<ScheduledReport?> CreateScheduledReportAsync(CreateScheduledReportRequest request);
    Task<ScheduledReport?> UpdateScheduledReportAsync(Guid id, UpdateScheduledReportRequest request);
    Task DeleteScheduledReportAsync(Guid id);
}

public class ReportService : BaseApiService, IReportService
{
    private const string BaseEndpoint = "api/reports";

    public ReportService(HttpClient http) : base(http) { }

    public async Task<List<RecentReport>> GetRecentReportsAsync()
    {
        return await GetAsync<List<RecentReport>>($"{BaseEndpoint}/recent")
            ?? new List<RecentReport>();
    }

    public async Task<List<ScheduledReport>> GetScheduledReportsAsync()
    {
        return await GetAsync<List<ScheduledReport>>($"{BaseEndpoint}/scheduled")
            ?? new List<ScheduledReport>();
    }

    public async Task<RecentReport?> GenerateReportAsync(GenerateReportRequest request)
    {
        return await PostAsync<GenerateReportRequest, RecentReport>($"{BaseEndpoint}/generate", request);
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
}
