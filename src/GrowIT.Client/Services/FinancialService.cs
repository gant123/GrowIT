using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IFinancialService
{
    Task<List<FundDto>> GetFundsAsync();
    Task<List<ProgramDto>> GetProgramsAsync();
    Task CreateFundAsync(CreateFundRequest request);
    Task UpdateFundAsync(Guid id, UpdateFundRequest request);
    Task CreateProgramAsync(CreateProgramRequest request);
}

public class FinancialService : BaseApiService, IFinancialService
{
    private const string BaseEndpoint = "api/financials";

    public FinancialService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<FundDto>> GetFundsAsync()
    {
        return await GetAsync<List<FundDto>>($"{BaseEndpoint}/funds") ?? new List<FundDto>();
    }

    public async Task<List<ProgramDto>> GetProgramsAsync()
    {
        return await GetAsync<List<ProgramDto>>($"{BaseEndpoint}/programs") ?? new List<ProgramDto>();
    }

    public async Task CreateFundAsync(CreateFundRequest request)
    {
        await PostAsync($"{BaseEndpoint}/funds", request);
    }

    public async Task UpdateFundAsync(Guid id, UpdateFundRequest request)
    {
        await PutAsync($"{BaseEndpoint}/funds/{id}", request);
    }

    public async Task CreateProgramAsync(CreateProgramRequest request)
    {
        await PostAsync($"{BaseEndpoint}/programs", request);
    }
}
