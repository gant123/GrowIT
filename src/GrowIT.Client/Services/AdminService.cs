using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IAdminService
{
    Task<OrganizationSettingsDto> GetOrganizationAsync(CancellationToken ct = default);
    Task<OrganizationSettingsDto> UpdateOrganizationAsync(UpdateOrganizationSettingsRequest request, CancellationToken ct = default);
    Task<List<AdminUserListItemDto>> GetUsersAsync(CancellationToken ct = default);
    Task<AdminUserListItemDto> UpdateUserRoleAsync(Guid userId, UpdateAdminUserRoleRequest request, CancellationToken ct = default);
    Task<AdminUserListItemDto> DeactivateUserAsync(Guid userId, CancellationToken ct = default);
    Task<AdminUserListItemDto> ReactivateUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<OrganizationInviteListItemDto>> GetInvitesAsync(CancellationToken ct = default);
    Task<List<InviteAuditNotificationDto>> GetInviteActivityAsync(int take = 25, CancellationToken ct = default);
    Task MarkInviteActivityReadAllAsync(CancellationToken ct = default);
    Task<List<AdminAuditLogItemDto>> GetAuditLogsAsync(int take = 100, string? table = null, string? action = null, CancellationToken ct = default);
    Task<EmailDiagnosticsDto> GetEmailDiagnosticsAsync(CancellationToken ct = default);
    Task<SystemDiagnosticsDto> GetSystemDiagnosticsAsync(CancellationToken ct = default);
    Task<SendTestEmailResponse> SendTestEmailAsync(SendTestEmailRequest request, CancellationToken ct = default);
    Task<List<SecurityAccessAttemptDto>> GetSecurityAccessAttemptsAsync(SecurityAccessAttemptQueryParams? query = null, CancellationToken ct = default);
    Task<CreateOrganizationInviteResponse> CreateInviteAsync(CreateOrganizationInviteRequest request, CancellationToken ct = default);
    Task<CreateOrganizationInviteResponse> ResendInviteAsync(Guid inviteId, CancellationToken ct = default);
    Task RevokeInviteAsync(Guid inviteId, CancellationToken ct = default);
}

public class AdminService : BaseApiService, IAdminService
{
    public AdminService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<OrganizationSettingsDto> GetOrganizationAsync(CancellationToken ct = default) =>
        (await GetAsync<OrganizationSettingsDto>("api/admin/organization", ct))!;

    public async Task<OrganizationSettingsDto> UpdateOrganizationAsync(UpdateOrganizationSettingsRequest request, CancellationToken ct = default) =>
        (await PutAsync<UpdateOrganizationSettingsRequest, OrganizationSettingsDto>("api/admin/organization", request, ct))!;

    public async Task<List<AdminUserListItemDto>> GetUsersAsync(CancellationToken ct = default) =>
        await GetAsync<List<AdminUserListItemDto>>("api/admin/users", ct) ?? [];

    public async Task<AdminUserListItemDto> UpdateUserRoleAsync(Guid userId, UpdateAdminUserRoleRequest request, CancellationToken ct = default) =>
        (await PutAsync<UpdateAdminUserRoleRequest, AdminUserListItemDto>($"api/admin/users/{userId}/role", request, ct))!;

    public async Task<AdminUserListItemDto> DeactivateUserAsync(Guid userId, CancellationToken ct = default) =>
        (await PostAsync<object, AdminUserListItemDto>($"api/admin/users/{userId}/deactivate", new { }, ct))!;

    public async Task<AdminUserListItemDto> ReactivateUserAsync(Guid userId, CancellationToken ct = default) =>
        (await PostAsync<object, AdminUserListItemDto>($"api/admin/users/{userId}/reactivate", new { }, ct))!;

    public async Task<List<OrganizationInviteListItemDto>> GetInvitesAsync(CancellationToken ct = default) =>
        await GetAsync<List<OrganizationInviteListItemDto>>("api/admin/invites", ct) ?? [];

    public async Task<List<InviteAuditNotificationDto>> GetInviteActivityAsync(int take = 25, CancellationToken ct = default) =>
        await GetAsync<List<InviteAuditNotificationDto>>($"api/admin/invite-activity?take={Math.Clamp(take, 1, 100)}", ct) ?? [];

    public Task MarkInviteActivityReadAllAsync(CancellationToken ct = default) =>
        PostAsync("api/admin/invite-activity/mark-all-read", new { }, ct);

    public async Task<List<AdminAuditLogItemDto>> GetAuditLogsAsync(int take = 100, string? table = null, string? action = null, CancellationToken ct = default)
    {
        var endpoint = $"api/admin/audit-logs?take={Math.Clamp(take, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(table))
            endpoint += $"&table={Uri.EscapeDataString(table)}";
        if (!string.IsNullOrWhiteSpace(action))
            endpoint += $"&action={Uri.EscapeDataString(action)}";

        return await GetAsync<List<AdminAuditLogItemDto>>(endpoint, ct) ?? [];
    }

    public async Task<EmailDiagnosticsDto> GetEmailDiagnosticsAsync(CancellationToken ct = default) =>
        (await GetAsync<EmailDiagnosticsDto>("api/admin/email-diagnostics", ct))!;

    public async Task<SystemDiagnosticsDto> GetSystemDiagnosticsAsync(CancellationToken ct = default) =>
        (await GetAsync<SystemDiagnosticsDto>("api/admin/system-diagnostics", ct))!;

    public async Task<SendTestEmailResponse> SendTestEmailAsync(SendTestEmailRequest request, CancellationToken ct = default) =>
        (await PostAsync<SendTestEmailRequest, SendTestEmailResponse>("api/admin/email-test", request, ct))!;

    public async Task<List<SecurityAccessAttemptDto>> GetSecurityAccessAttemptsAsync(SecurityAccessAttemptQueryParams? query = null, CancellationToken ct = default) =>
        await GetAsync<List<SecurityAccessAttemptDto>>($"api/admin/security-attempts{BuildSecurityQuery(query)}", ct) ?? [];

    public async Task<CreateOrganizationInviteResponse> CreateInviteAsync(CreateOrganizationInviteRequest request, CancellationToken ct = default) =>
        (await PostAsync<CreateOrganizationInviteRequest, CreateOrganizationInviteResponse>("api/admin/invites", request, ct))!;

    public async Task<CreateOrganizationInviteResponse> ResendInviteAsync(Guid inviteId, CancellationToken ct = default) =>
        (await PostAsync<object, CreateOrganizationInviteResponse>($"api/admin/invites/{inviteId}/resend", new { }, ct))!;

    public Task RevokeInviteAsync(Guid inviteId, CancellationToken ct = default) =>
        DeleteAsync($"api/admin/invites/{inviteId}", ct);

    private static string BuildSecurityQuery(SecurityAccessAttemptQueryParams? query)
    {
        if (query is null) return string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Ip))
        {
            parts.Add($"ip={Uri.EscapeDataString(query.Ip.Trim())}");
        }

        if (query.Take.HasValue)
        {
            parts.Add($"take={Math.Clamp(query.Take.Value, 1, 1000)}");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
