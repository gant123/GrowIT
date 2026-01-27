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
    Task<InvestmentDetailDto> CreateInvestmentAsync(InvestmentCreateDto dto);
    Task<InvestmentDetailDto> UpdateInvestmentAsync(Guid id, InvestmentUpdateDto dto);
    Task DeleteInvestmentAsync(Guid id);
    Task<InvestmentDetailDto> ApproveInvestmentAsync(Guid id, string approvedBy);
    Task<InvestmentDetailDto> DisburseInvestmentAsync(Guid id);
    Task<List<InvestmentSummaryByCategory>> GetSummaryByCategoryAsync(string fiscalYear);
    Task<List<InvestmentSummaryByFundingSource>> GetSummaryByFundingSourceAsync(string fiscalYear);
}

/// <summary>
/// Query parameters for filtering investments.
/// Uses InvestmentStatus and InvestmentCategory from GrowIT.Shared.DTOs.
/// </summary>
public class InvestmentQueryParams
{
    public Guid? PersonId { get; set; }
    public string? FiscalYear { get; set; }
    public InvestmentStatus? Status { get; set; }
    public InvestmentCategory? Category { get; set; }
    public string? FundingSource { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? SearchTerm { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class InvestmentService : BaseApiService, IInvestmentService
{
    private const string BaseEndpoint = "api/investments";

    public InvestmentService(HttpClient http) : base(http) { }

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

    public async Task<InvestmentDetailDto> CreateInvestmentAsync(InvestmentCreateDto dto)
    {
        return await PostAsync<InvestmentCreateDto, InvestmentDetailDto>(BaseEndpoint, dto) 
            ?? throw new ApiException("Failed to create investment");
    }

    public async Task<InvestmentDetailDto> UpdateInvestmentAsync(Guid id, InvestmentUpdateDto dto)
    {
        return await PutAsync<InvestmentUpdateDto, InvestmentDetailDto>($"{BaseEndpoint}/{id}", dto) 
            ?? throw new ApiException("Failed to update investment");
    }

    public async Task DeleteInvestmentAsync(Guid id)
    {
        await DeleteAsync($"{BaseEndpoint}/{id}");
    }

    public async Task<InvestmentDetailDto> ApproveInvestmentAsync(Guid id, string approvedBy)
    {
        return await PostAsync<object, InvestmentDetailDto>($"{BaseEndpoint}/{id}/approve", new { approvedBy }) 
            ?? throw new ApiException("Failed to approve investment");
    }

    public async Task<InvestmentDetailDto> DisburseInvestmentAsync(Guid id)
    {
        return await PostAsync<object, InvestmentDetailDto>($"{BaseEndpoint}/{id}/disburse", new { }) 
            ?? throw new ApiException("Failed to disburse investment");
    }

    public async Task<List<InvestmentSummaryByCategory>> GetSummaryByCategoryAsync(string fiscalYear)
    {
        return await GetAsync<List<InvestmentSummaryByCategory>>($"{BaseEndpoint}/summary/category?fiscalYear={fiscalYear}") 
            ?? new List<InvestmentSummaryByCategory>();
    }

    public async Task<List<InvestmentSummaryByFundingSource>> GetSummaryByFundingSourceAsync(string fiscalYear)
    {
        return await GetAsync<List<InvestmentSummaryByFundingSource>>($"{BaseEndpoint}/summary/funding-source?fiscalYear={fiscalYear}") 
            ?? new List<InvestmentSummaryByFundingSource>();
    }

    private static string BuildQueryString(InvestmentQueryParams query)
    {
        var parts = new List<string>();
        
        if (query.PersonId.HasValue)
            parts.Add($"personId={query.PersonId}");
        if (!string.IsNullOrEmpty(query.FiscalYear))
            parts.Add($"fiscalYear={query.FiscalYear}");
        if (query.Status.HasValue)
            parts.Add($"status={query.Status}");
        if (query.Category.HasValue)
            parts.Add($"category={query.Category}");
        if (!string.IsNullOrEmpty(query.FundingSource))
            parts.Add($"fundingSource={Uri.EscapeDataString(query.FundingSource)}");
        if (query.DateFrom.HasValue)
            parts.Add($"dateFrom={query.DateFrom:yyyy-MM-dd}");
        if (query.DateTo.HasValue)
            parts.Add($"dateTo={query.DateTo:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(query.SearchTerm))
            parts.Add($"search={Uri.EscapeDataString(query.SearchTerm)}");
        if (!string.IsNullOrEmpty(query.SortBy))
            parts.Add($"sortBy={query.SortBy}");
        if (query.SortDescending)
            parts.Add("sortDesc=true");
        
        parts.Add($"page={query.PageNumber}");
        parts.Add($"pageSize={query.PageSize}");
        
        return string.Join("&", parts);
    }
}