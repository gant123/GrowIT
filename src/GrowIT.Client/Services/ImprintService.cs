using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IImprintService
{
    Task<List<ImprintListDto>> GetImprintsAsync(int page = 1, int pageSize = 50);
    Task<ImprintResponseDto?> GetImprintByIdAsync(Guid id);
    Task<List<ImprintResponseDto>> GetImprintsForMemberAsync(Guid memberId);
    Task<ImprintResponseDto?> CreateImprintAsync(CreateImprintRequest request);
}

public class ImprintService : BaseApiService, IImprintService
{
    private const string BaseEndpoint = "api/imprints";

    public ImprintService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<ImprintListDto>> GetImprintsAsync(int page = 1, int pageSize = 50)
    {
        return await GetAsync<List<ImprintListDto>>($"{BaseEndpoint}?page={page}&pageSize={pageSize}")
            ?? new List<ImprintListDto>();
    }

    public async Task<ImprintResponseDto?> GetImprintByIdAsync(Guid id)
    {
        return await GetAsync<ImprintResponseDto>($"{BaseEndpoint}/{id}");
    }

    public async Task<List<ImprintResponseDto>> GetImprintsForMemberAsync(Guid memberId)
    {
        return await GetAsync<List<ImprintResponseDto>>($"{BaseEndpoint}/member/{memberId}")
            ?? new List<ImprintResponseDto>();
    }

    public async Task<ImprintResponseDto?> CreateImprintAsync(CreateImprintRequest request)
    {
        return await PostAsync<CreateImprintRequest, ImprintResponseDto>(BaseEndpoint, request);
    }
}
