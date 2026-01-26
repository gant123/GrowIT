using System.Net.Http.Json;
using GrowIT.Shared.DTOs;   // <--- Added this
using GrowIT.Shared.Enums;  // <--- Added this

namespace GrowIT.Client.Services;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync();
    Task<List<InvestmentListDto>> GetRecentInvestmentsAsync();
    Task<InvestmentSummaryByCategory> GetInvestmentTrendsAsync();
}

public class DashboardService : BaseApiService, IDashboardService
{
    public DashboardService(HttpClient http) : base(http) { }

    public async Task<DashboardStatsDto> GetStatsAsync()
    {
        return await _http.GetFromJsonAsync<DashboardStatsDto>("api/dashboard") 
               ?? new DashboardStatsDto();
    }

    public async Task<List<InvestmentListDto>> GetRecentInvestmentsAsync()
    {
        // Re-using the investment endpoint with page size 5 sorted by date
        var result = await _http.GetFromJsonAsync<PaginatedResult<InvestmentListDto>>("api/investments?pageSize=5&sortBy=Date&sortDescending=true");
        return result?.Items ?? new List<InvestmentListDto>();
    }

    public async Task<InvestmentSummaryByCategory> GetInvestmentTrendsAsync()
    {
        return await _http.GetFromJsonAsync<InvestmentSummaryByCategory>("api/investments/summary/category") 
               ?? new InvestmentSummaryByCategory();
    }
}