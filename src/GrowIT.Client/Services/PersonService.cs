using GrowIT.Client.Models;

namespace GrowIT.Client.Services;

/// <summary>
/// Service for managing people/clients in the grow.IT system.
/// </summary>
public interface IPersonService
{
    Task<PaginatedResponse<PersonListDto>> GetPeopleAsync(PersonQueryParams query);
    Task<PersonDetailDto?> GetPersonAsync(Guid id);
    Task<PersonDetailDto> CreatePersonAsync(PersonCreateDto dto);
    Task<PersonDetailDto> UpdatePersonAsync(Guid id, PersonUpdateDto dto);
    Task DeletePersonAsync(Guid id);
    Task<List<PersonListDto>> SearchPeopleAsync(string searchTerm);
    Task<PersonListDto> UpdateSeasonAsync(Guid id, Season season);
    Task<PersonListDto> UpdateStabilityScoreAsync(Guid id, int score);
}

public class PersonQueryParams
{
    public string? SearchTerm { get; set; }
    public Season? Season { get; set; }
    public bool? IsActive { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class PersonService : BaseApiService, IPersonService
{
    private const string BaseEndpoint = "/api/people";

    public PersonService(HttpClient httpClient) : base(httpClient) { }

    public async Task<PaginatedResponse<PersonListDto>> GetPeopleAsync(PersonQueryParams query)
    {
        var queryString = BuildQueryString(query);
        return await GetAsync<PaginatedResponse<PersonListDto>>($"{BaseEndpoint}?{queryString}") 
            ?? new PaginatedResponse<PersonListDto>();
    }

    public async Task<PersonDetailDto?> GetPersonAsync(Guid id)
    {
        return await GetAsync<PersonDetailDto>($"{BaseEndpoint}/{id}");
    }

    public async Task<PersonDetailDto> CreatePersonAsync(PersonCreateDto dto)
    {
        return await PostAsync<PersonCreateDto, PersonDetailDto>(BaseEndpoint, dto) 
            ?? throw new ApiException("Failed to create person");
    }

    public async Task<PersonDetailDto> UpdatePersonAsync(Guid id, PersonUpdateDto dto)
    {
        return await PutAsync<PersonUpdateDto, PersonDetailDto>($"{BaseEndpoint}/{id}", dto) 
            ?? throw new ApiException("Failed to update person");
    }

    public async Task DeletePersonAsync(Guid id)
    {
        await DeleteAsync($"{BaseEndpoint}/{id}");
    }

    public async Task<List<PersonListDto>> SearchPeopleAsync(string searchTerm)
    {
        return await GetAsync<List<PersonListDto>>($"{BaseEndpoint}/search?q={Uri.EscapeDataString(searchTerm)}") 
            ?? new List<PersonListDto>();
    }

    public async Task<PersonListDto> UpdateSeasonAsync(Guid id, Season season)
    {
        return await PutAsync<object, PersonListDto>($"{BaseEndpoint}/{id}/season", new { season }) 
            ?? throw new ApiException("Failed to update season");
    }

    public async Task<PersonListDto> UpdateStabilityScoreAsync(Guid id, int score)
    {
        return await PutAsync<object, PersonListDto>($"{BaseEndpoint}/{id}/stability", new { score }) 
            ?? throw new ApiException("Failed to update stability score");
    }

    private static string BuildQueryString(PersonQueryParams query)
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrEmpty(query.SearchTerm))
            parts.Add($"search={Uri.EscapeDataString(query.SearchTerm)}");
        if (query.Season.HasValue)
            parts.Add($"season={query.Season}");
        if (query.IsActive.HasValue)
            parts.Add($"isActive={query.IsActive}");
        if (!string.IsNullOrEmpty(query.SortBy))
            parts.Add($"sortBy={query.SortBy}");
        if (query.SortDescending)
            parts.Add("sortDesc=true");
        
        parts.Add($"page={query.PageNumber}");
        parts.Add($"pageSize={query.PageSize}");
        
        return string.Join("&", parts);
    }
}
