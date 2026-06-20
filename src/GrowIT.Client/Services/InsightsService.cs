using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IInsightsService
{
    Task<InsightsDto> GetInsightsAsync(CancellationToken ct = default);
}

public class InsightsService : BaseApiService, IInsightsService
{
    public InsightsService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<InsightsDto> GetInsightsAsync(CancellationToken ct = default) =>
        await GetAsync<InsightsDto>("api/dashboard/insights", ct) ?? new InsightsDto();
}
