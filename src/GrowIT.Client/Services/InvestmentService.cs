using System.Net.Http.Json;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Client.Services;

public interface IInvestmentService
{
    Task<List<InvestmentListDto>> GetAllInvestmentsAsync();
    Task<Guid> CreateInvestmentAsync(CreateInvestmentRequest request);
    Task ApproveInvestmentAsync(Guid id);
}

public class InvestmentService : BaseApiService, IInvestmentService
{
    public InvestmentService(HttpClient http) : base(http) { }

    public async Task<List<InvestmentListDto>> GetAllInvestmentsAsync()
    {
        // Calls api/investments which returns List<InvestmentListDto>
        return await _http.GetFromJsonAsync<List<InvestmentListDto>>("api/investments") 
               ?? new List<InvestmentListDto>();
    }

    public async Task<Guid> CreateInvestmentAsync(CreateInvestmentRequest request)
    {
        var response = await _http.PostAsJsonAsync("api/investments", request);
        response.EnsureSuccessStatusCode();
        // Assuming returns Guid or object with Id
        return Guid.Empty; // Placeholder, adjust if your controller returns ID
    }

    public async Task ApproveInvestmentAsync(Guid id)
    {
        var response = await _http.PostAsync($"api/investments/{id}/approve", null);
        response.EnsureSuccessStatusCode();
    }
}