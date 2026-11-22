using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Client.Services;

public interface IHouseholdService
{
    Task<List<HouseholdDto>> GetHouseholdsAsync();
    Task<Guid> CreateHouseholdAsync(CreateHouseholdRequest request);
    Task<string> AddMemberAsync(Guid householdId, Guid clientId, HouseholdRole role);
    Task<string> RenameHouseholdAsync(Guid householdId, string name);
    Task<string> DeleteHouseholdAsync(Guid householdId);
    Task<string> RemoveMemberAsync(Guid householdId, Guid clientId);
}

public class HouseholdService : BaseApiService, IHouseholdService
{
    private const string BaseEndpoint = "api/households";

    public HouseholdService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<HouseholdDto>> GetHouseholdsAsync()
    {
        return await GetAsync<List<HouseholdDto>>(BaseEndpoint) ?? new List<HouseholdDto>();
    }

    public async Task<Guid> CreateHouseholdAsync(CreateHouseholdRequest request)
    {
        var result = await PostAsync<CreateHouseholdRequest, CreateHouseholdResponseDto>(BaseEndpoint, request);
        return result?.HouseholdId ?? Guid.Empty;
    }

    public async Task<string> AddMemberAsync(Guid householdId, Guid clientId, HouseholdRole role)
    {
        var result = await PostAsync<object, MessageResponse>(
            $"{BaseEndpoint}/{householdId}/add-member/{clientId}?role={Uri.EscapeDataString(role.ToString())}", new { });
        return result?.Message ?? "Household updated.";
    }

    public async Task<string> RenameHouseholdAsync(Guid householdId, string name)
    {
        var result = await PutAsync<UpdateHouseholdRequest, MessageResponse>(
            $"{BaseEndpoint}/{householdId}", new UpdateHouseholdRequest { Name = name });
        return result?.Message ?? "Household renamed.";
    }

    public async Task<string> DeleteHouseholdAsync(Guid householdId)
    {
        var result = await DeleteAsync<MessageResponse>($"{BaseEndpoint}/{householdId}");
        return result?.Message ?? "Household deleted.";
    }

    public async Task<string> RemoveMemberAsync(Guid householdId, Guid clientId)
    {
        var result = await DeleteAsync<MessageResponse>($"{BaseEndpoint}/{householdId}/members/{clientId}");
        return result?.Message ?? "Removed from the household.";
    }
}
