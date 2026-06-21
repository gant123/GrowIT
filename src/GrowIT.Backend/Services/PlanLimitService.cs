using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Services;

/// <summary>
/// Resolves the current tenant's effective subscription plan and reports usage against
/// its seat/record limits. Used to enforce plan limits on create operations and to show
/// usage in the UI.
/// </summary>
/// <remarks>
/// Limits are enforced as <em>soft</em> caps: callers read usage, then create. This
/// check-then-create pattern is not atomic, so two concurrent creates from the same
/// tenant could momentarily exceed a cap by one. This is acceptable for plan/seat
/// limits (no safety or correctness impact, and the next read reflects the true count).
/// If a contractually-binding hard cap is ever required, add a row lock on the tenant
/// or a database CHECK constraint here.
/// </remarks>
public interface IPlanLimitService
{
    Task<PlanUsageDto> GetUsageAsync(CancellationToken cancellationToken = default);
}

public sealed class PlanLimitService : IPlanLimitService
{
    // Used only if no plan can be resolved at all; mirrors the default Free plan.
    private const int FallbackMaxUsers = 2;
    private const int FallbackMaxClients = 25;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentTenantService _tenantService;

    public PlanLimitService(ApplicationDbContext db, ICurrentTenantService tenantService)
    {
        _db = db;
        _tenantService = tenantService;
    }

    public async Task<PlanUsageDto> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return new PlanUsageDto { PlanName = "Free", ClientsMax = FallbackMaxClients, UsersMax = FallbackMaxUsers };
        }

        var (planName, maxUsers, maxClients) = await ResolvePlanAsync(tenantId.Value, cancellationToken);

        // These DbSets are tenant-scoped by the global query filter, so counts are per-tenant.
        var clientsUsed = await _db.Clients.CountAsync(cancellationToken);
        var activeUsers = await _db.Users.CountAsync(u => u.IsActive, cancellationToken);
        var pendingInvites = await _db.OrganizationInvites
            .CountAsync(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > DateTime.UtcNow, cancellationToken);

        return new PlanUsageDto
        {
            PlanName = planName,
            ClientsUsed = clientsUsed,
            ClientsMax = maxClients,
            UsersUsed = activeUsers + pendingInvites,
            UsersMax = maxUsers,
        };
    }

    private async Task<(string Name, int MaxUsers, int MaxClients)> ResolvePlanAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        // 1. An active (or trialing) subscription's plan is authoritative.
        var activePlan = await _db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            .OrderByDescending(s => s.StartDate)
            .Select(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);
        if (activePlan is not null)
        {
            return (activePlan.Name, activePlan.MaxUsers, activePlan.MaxClients);
        }

        // 2. Otherwise fall back to the plan named after the tenant's SubscriptionPlan tier.
        var tier = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => (SubscriptionPlanType?)t.SubscriptionPlan)
            .FirstOrDefaultAsync(cancellationToken);
        var planName = (tier ?? SubscriptionPlanType.Free).ToString();

        var named = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == planName, cancellationToken);
        if (named is not null)
        {
            return (named.Name, named.MaxUsers, named.MaxClients);
        }

        // 3. Nothing configured — fail open to the Free defaults.
        return ("Free", FallbackMaxUsers, FallbackMaxClients);
    }
}
