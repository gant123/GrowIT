using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IOrganizationReadinessService
{
    Task<AscScoreDto> GetAscScoreAsync(CancellationToken ct = default);
}

public class OrganizationReadinessService : BaseApiService, IOrganizationReadinessService
{
    public OrganizationReadinessService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<AscScoreDto> GetAscScoreAsync(CancellationToken ct = default) =>
        await GetAsync<AscScoreDto>("api/organization-readiness/asc-score", ct) ?? new AscScoreDto();
}
