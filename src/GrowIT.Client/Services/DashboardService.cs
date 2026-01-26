using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

/// <summary>
/// Service for dashboard metrics and aggregated data.
/// Uses DashboardStatsDto from GrowIT.Shared.DTOs.
/// Calls GET api/dashboard endpoint.
/// </summary>
public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync();
}

public class DashboardService : BaseApiService, IDashboardService
{
    private const string BaseEndpoint = "api/dashboard";

    public DashboardService(HttpClient http) : base(http) { }

    /// <summary>
    /// Gets the main dashboard stats from api/dashboard endpoint.
    /// Returns DashboardStatsDto with:
    /// - TotalInvestedYtd, HouseholdsServedYtd, ActiveCases, FundsAvailable
    /// - MonthlyTrends (List of MonthlyMetric)
    /// - RecentActivity (List of ActivityItem)
    /// - PendingFollowUps (List of TaskItem)
    /// </summary>
    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        return await GetAsync<DashboardStatsDto>(BaseEndpoint) 
            ?? new DashboardStatsDto();
    }
}