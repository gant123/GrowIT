using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IGrowthPlanService
{
    Task<List<GrowthPlanListDto>> GetGrowthPlansAsync();
    Task<GrowthPlanListDto?> GetGrowthPlanAsync(Guid id);
    Task<GrowthPlanListDto?> CreateGrowthPlanAsync(CreateGrowthPlanRequest request);
    Task<GrowthPlanListDto?> UpdateGrowthPlanAsync(Guid id, UpdateGrowthPlanRequest request);
    Task DeleteGrowthPlanAsync(Guid id);
}

public class GrowthPlanService : BaseApiService, IGrowthPlanService
{
    private const string BaseEndpoint = "api/growthplans";

    public GrowthPlanService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<GrowthPlanListDto>> GetGrowthPlansAsync()
    {
        return await GetAsync<List<GrowthPlanListDto>>(BaseEndpoint) ?? new List<GrowthPlanListDto>();
    }

    public async Task<GrowthPlanListDto?> GetGrowthPlanAsync(Guid id)
    {
        return await GetAsync<GrowthPlanListDto>($"{BaseEndpoint}/{id}");
    }

    public async Task<GrowthPlanListDto?> CreateGrowthPlanAsync(CreateGrowthPlanRequest request)
    {
        return await PostAsync<CreateGrowthPlanRequest, GrowthPlanListDto>(BaseEndpoint, request);
    }

    public async Task<GrowthPlanListDto?> UpdateGrowthPlanAsync(Guid id, UpdateGrowthPlanRequest request)
    {
        return await PutAsync<UpdateGrowthPlanRequest, GrowthPlanListDto>($"{BaseEndpoint}/{id}", request);
    }

    public async Task DeleteGrowthPlanAsync(Guid id)
    {
        await DeleteAsync($"{BaseEndpoint}/{id}");
    }
}
