using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Client.Services;

public interface IHouseholdService
{
    Task<List<HouseholdDto>> GetHouseholdsAsync();
    Task<Guid> CreateHouseholdAsync(CreateHouseholdRequest request);
    Task AddMemberAsync(Guid householdId, Guid clientId, HouseholdRole role);
}

public class HouseholdService : BaseApiService, IHouseholdService
{
    private const string BaseEndpoint = "api/households";

    public HouseholdService(HttpClient http) : base(http) { }

    public async Task<List<HouseholdDto>> GetHouseholdsAsync()
    {
        return await GetAsync<List<HouseholdDto>>(BaseEndpoint) ?? new List<HouseholdDto>();
    }

    public async Task<Guid> CreateHouseholdAsync(CreateHouseholdRequest request)
    {
        var result = await PostAsync<CreateHouseholdRequest, CreateHouseholdResponseDto>(BaseEndpoint, request);
        return result?.HouseholdId ?? Guid.Empty;
    }

    public async Task AddMemberAsync(Guid householdId, Guid clientId, HouseholdRole role)
    {
        await PostAsync($"{BaseEndpoint}/{householdId}/add-member/{clientId}?role={Uri.EscapeDataString(role.ToString())}", new { });
    }
}
