using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace GrowIT.Client.Services;

public sealed class RoleAccessSnapshot
{
    public bool IsAuthenticated { get; init; }
    public string PrimaryRole { get; init; } = "Guest";
    public IReadOnlyList<string> Roles { get; init; } = [];

    public bool IsOwner { get; init; }
    public bool IsSuperAdmin { get; init; }
    public bool IsAdmin { get; init; }
    public bool IsManager { get; init; }
    public bool IsCaseManager { get; init; }
    public bool IsAnalyst { get; init; }

    public bool CanManageAdminWorkspace => IsOwner || IsAdmin || IsManager;
    public bool CanManageSiteContent => IsSuperAdmin || IsOwner;
    public bool CanAccessReports => IsOwner || IsAdmin || IsManager;
    public bool CanManageFinancials => IsOwner || IsAdmin || IsManager;
    public bool CanSeedDemoData => IsOwner || IsAdmin;
    public bool CanDocumentServiceRecords => IsOwner || IsAdmin || IsManager || IsCaseManager;
    public bool CanApproveServiceInvestments => IsOwner || IsAdmin || IsManager;
}

public interface IRoleAccessService
{
    Task<RoleAccessSnapshot> GetAccessAsync();
    Task<bool> HasAnyRoleAsync(params string[] roles);
}

public sealed class RoleAccessService : IRoleAccessService
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public RoleAccessService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public async Task<RoleAccessSnapshot> GetAccessAsync()
    {
        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            return new RoleAccessSnapshot();
        }

        var roles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primaryRole = roles.FirstOrDefault() ?? "User";

        return new RoleAccessSnapshot
        {
            IsAuthenticated = true,
            PrimaryRole = primaryRole,
            Roles = roles,
            IsOwner = ContainsRole(roles, "Owner"),
            IsSuperAdmin = ContainsRole(roles, "SuperAdmin"),
            IsAdmin = ContainsRole(roles, "Admin"),
            IsManager = ContainsRole(roles, "Manager"),
            IsCaseManager = ContainsRole(roles, "Case Manager"),
            IsAnalyst = ContainsRole(roles, "Analyst")
        };
    }

    public async Task<bool> HasAnyRoleAsync(params string[] roles)
    {
        var snapshot = await GetAccessAsync();
        if (!snapshot.IsAuthenticated) return false;
        return roles.Any(role => ContainsRole(snapshot.Roles, role));
    }

    private static bool ContainsRole(IEnumerable<string> roles, string role) =>
        roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}
