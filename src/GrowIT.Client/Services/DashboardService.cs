using GrowIT.Client.Models;

namespace GrowIT.Client.Services;

/// <summary>
/// Service for dashboard metrics and aggregated data.
/// </summary>
public interface IDashboardService
{
    Task<ImpactDashboardDto> GetDashboardDataAsync(string fiscalYear);
    Task<List<InvestmentListDto>> GetRecentInvestmentsAsync(int count = 5);
    Task<List<PersonListDto>> GetPeopleNeedingAttentionAsync(int count = 5);
    Task<List<ImprintListDto>> GetRecentImprintsAsync(int count = 5);
    Task<List<InvestmentSummaryByCategory>> GetInvestmentsByCategoryAsync(string fiscalYear);
    Task<SeasonDistribution> GetSeasonDistributionAsync();
}

public class SeasonDistribution
{
    public int Crisis { get; set; }
    public int Planting { get; set; }
    public int Growing { get; set; }
    public int Harvest { get; set; }
    public int Total => Crisis + Planting + Growing + Harvest;
}

public class DashboardService : BaseApiService, IDashboardService
{
    private const string BaseEndpoint = "/api/dashboard";

    public DashboardService(HttpClient httpClient) : base(httpClient) { }

    public async Task<ImpactDashboardDto> GetDashboardDataAsync(string fiscalYear)
    {
        return await GetAsync<ImpactDashboardDto>($"{BaseEndpoint}?fiscalYear={fiscalYear}") 
            ?? new ImpactDashboardDto();
    }

    public async Task<List<InvestmentListDto>> GetRecentInvestmentsAsync(int count = 5)
    {
        return await GetAsync<List<InvestmentListDto>>($"{BaseEndpoint}/recent-investments?count={count}") 
            ?? new List<InvestmentListDto>();
    }

    public async Task<List<PersonListDto>> GetPeopleNeedingAttentionAsync(int count = 5)
    {
        return await GetAsync<List<PersonListDto>>($"{BaseEndpoint}/needs-attention?count={count}") 
            ?? new List<PersonListDto>();
    }

    public async Task<List<ImprintListDto>> GetRecentImprintsAsync(int count = 5)
    {
        return await GetAsync<List<ImprintListDto>>($"{BaseEndpoint}/recent-imprints?count={count}") 
            ?? new List<ImprintListDto>();
    }

    public async Task<List<InvestmentSummaryByCategory>> GetInvestmentsByCategoryAsync(string fiscalYear)
    {
        return await GetAsync<List<InvestmentSummaryByCategory>>($"{BaseEndpoint}/investments-by-category?fiscalYear={fiscalYear}") 
            ?? new List<InvestmentSummaryByCategory>();
    }

    public async Task<SeasonDistribution> GetSeasonDistributionAsync()
    {
        return await GetAsync<SeasonDistribution>($"{BaseEndpoint}/season-distribution") 
            ?? new SeasonDistribution();
    }
}
