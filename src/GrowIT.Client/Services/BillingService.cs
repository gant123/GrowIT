using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Client.Services;

public interface IBillingService
{
    Task<BillingOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<PlanUsageDto> GetUsageAsync(CancellationToken ct = default);
    Task<string> CreateCheckoutSessionAsync(Guid planId, BillingInterval interval, CancellationToken ct = default);
    Task<PlanChangeResultDto> ActivatePlanAsync(Guid planId, CancellationToken ct = default);
    Task<string> CreatePortalSessionAsync(CancellationToken ct = default);
    Task CreatePlanAsync(CreateSubscriptionPlanRequest request, CancellationToken ct = default);
    Task UpdatePlanAsync(Guid id, UpdateSubscriptionPlanRequest request, CancellationToken ct = default);
}

public class BillingService : BaseApiService, IBillingService
{
    private const string Endpoint = "api/billing";

    public BillingService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<BillingOverviewDto> GetOverviewAsync(CancellationToken ct = default) =>
        await GetAsync<BillingOverviewDto>($"{Endpoint}/overview", ct) ?? new BillingOverviewDto();

    public async Task<PlanUsageDto> GetUsageAsync(CancellationToken ct = default) =>
        await GetAsync<PlanUsageDto>($"{Endpoint}/usage", ct) ?? new PlanUsageDto();

    public async Task<string> CreateCheckoutSessionAsync(Guid planId, BillingInterval interval, CancellationToken ct = default)
    {
        var result = await PostAsync<CreateCheckoutSessionRequest, BillingRedirectResponse>(
            $"{Endpoint}/checkout-session",
            new CreateCheckoutSessionRequest { PlanId = planId, Interval = interval },
            ct);

        return result?.Url ?? string.Empty;
    }

    public async Task<PlanChangeResultDto> ActivatePlanAsync(Guid planId, CancellationToken ct = default) =>
        await PostAsync<ActivatePlanRequest, PlanChangeResultDto>(
            $"{Endpoint}/activate-plan",
            new ActivatePlanRequest { PlanId = planId },
            ct) ?? new PlanChangeResultDto();

    public async Task<string> CreatePortalSessionAsync(CancellationToken ct = default)
    {
        var result = await PostAsync<object, BillingRedirectResponse>($"{Endpoint}/portal-session", new { }, ct);
        return result?.Url ?? string.Empty;
    }

    public Task CreatePlanAsync(CreateSubscriptionPlanRequest request, CancellationToken ct = default) =>
        PostAsync($"{Endpoint}/plans", request, ct);

    public Task UpdatePlanAsync(Guid id, UpdateSubscriptionPlanRequest request, CancellationToken ct = default) =>
        PutAsync($"{Endpoint}/plans/{id}", request, ct);
}
