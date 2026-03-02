using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Client.Services;

/// <summary>
/// Service for managing investments in the grow.IT system.
/// Uses DTOs from GrowIT.Shared.DTOs (InvestmentListDto, InvestmentStatus, InvestmentCategory, PaginatedResult).
/// </summary>
public interface IInvestmentService
{
    Task<PaginatedResult<InvestmentListDto>> GetInvestmentsAsync(InvestmentQueryParams query);
    Task<InvestmentDetailDto?> GetInvestmentAsync(Guid id);
    Task<Guid> CreateInvestmentAsync(CreateInvestmentRequest request);
    Task DeleteInvestmentAsync(Guid id);
    Task ApproveInvestmentAsync(Guid id, string approvedBy);
    Task DisburseInvestmentAsync(Guid id);
    Task ReassignInvestmentAsync(Guid id, ReassignInvestmentRequest request);
}

public class InvestmentService : BaseApiService, IInvestmentService
{
    private const string BaseEndpoint = "api/investments";

    public InvestmentService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<PaginatedResult<InvestmentListDto>> GetInvestmentsAsync(InvestmentQueryParams query)
    {
        var queryString = BuildQueryString(query);
        return await GetAsync<PaginatedResult<InvestmentListDto>>($"{BaseEndpoint}?{queryString}") 
            ?? new PaginatedResult<InvestmentListDto>();
    }

    public async Task<InvestmentDetailDto?> GetInvestmentAsync(Guid id)
    {
        return await GetAsync<InvestmentDetailDto>($"{BaseEndpoint}/{id}");
    }

    public async Task<Guid> CreateInvestmentAsync(CreateInvestmentRequest request)
    {
        var result = await PostAsync<CreateInvestmentRequest, InvestmentCreateResponse>(BaseEndpoint, request);
        return result?.InvestmentId ?? Guid.Empty;
    }

    public async Task DeleteInvestmentAsync(Guid id)
    {
        await DeleteAsync($"{BaseEndpoint}/{id}");
    }

    public async Task ApproveInvestmentAsync(Guid id, string approvedBy)
    {
        await PostAsync($"{BaseEndpoint}/{id}/approve", new ApproveInvestmentRequest { ApprovedBy = approvedBy });
    }

    public async Task DisburseInvestmentAsync(Guid id)
    {
        await PostAsync($"{BaseEndpoint}/{id}/disburse", new { });
    }

    public async Task ReassignInvestmentAsync(Guid id, ReassignInvestmentRequest request)
    {
        await PatchAsync<ReassignInvestmentRequest, object>($"{BaseEndpoint}/{id}/reassign", request);
    }

    private static string BuildQueryString(InvestmentQueryParams query)
    {
        var parts = new List<string>();
        
        if (query.PersonId.HasValue)
            parts.Add($"personId={query.PersonId}");
        if (query.Status.HasValue)
            parts.Add($"status={query.Status}");
        if (!string.IsNullOrEmpty(query.SearchTerm))
            parts.Add($"searchTerm={Uri.EscapeDataString(query.SearchTerm)}");
        
        parts.Add($"pageNumber={query.PageNumber}");
        parts.Add($"pageSize={query.PageSize}");
        
        return string.Join("&", parts);
    }

    private class InvestmentCreateResponse { public Guid InvestmentId { get; set; } }
}
