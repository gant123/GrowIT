using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface IAdminService
{
    Task<OrganizationSettingsDto> GetOrganizationAsync();
    Task<OrganizationSettingsDto> UpdateOrganizationAsync(UpdateOrganizationSettingsRequest request);
    Task<List<AdminUserListItemDto>> GetUsersAsync();
    Task<AdminUserListItemDto> UpdateUserRoleAsync(Guid userId, UpdateAdminUserRoleRequest request);
    Task<AdminUserListItemDto> DeactivateUserAsync(Guid userId);
    Task<AdminUserListItemDto> ReactivateUserAsync(Guid userId);
    Task<List<OrganizationInviteListItemDto>> GetInvitesAsync();
    Task<List<InviteAuditNotificationDto>> GetInviteActivityAsync(int take = 25);
    Task MarkInviteActivityReadAllAsync();
    Task<List<AdminAuditLogItemDto>> GetAuditLogsAsync(int take = 100, string? table = null, string? action = null);
    Task<SeedDemoDataResponseDto> SeedDemoDataAsync();
    Task<CreateOrganizationInviteResponse> CreateInviteAsync(CreateOrganizationInviteRequest request);
    Task<CreateOrganizationInviteResponse> ResendInviteAsync(Guid inviteId);
    Task RevokeInviteAsync(Guid inviteId);
}

public class AdminService : BaseApiService, IAdminService
{
    public AdminService(HttpClient http) : base(http) { }

    public async Task<OrganizationSettingsDto> GetOrganizationAsync() =>
        (await GetAsync<OrganizationSettingsDto>("api/admin/organization"))!;

    public async Task<OrganizationSettingsDto> UpdateOrganizationAsync(UpdateOrganizationSettingsRequest request) =>
        (await PutAsync<UpdateOrganizationSettingsRequest, OrganizationSettingsDto>("api/admin/organization", request))!;

    public async Task<List<AdminUserListItemDto>> GetUsersAsync() =>
        await GetAsync<List<AdminUserListItemDto>>("api/admin/users") ?? [];

    public async Task<AdminUserListItemDto> UpdateUserRoleAsync(Guid userId, UpdateAdminUserRoleRequest request) =>
        (await PutAsync<UpdateAdminUserRoleRequest, AdminUserListItemDto>($"api/admin/users/{userId}/role", request))!;

    public async Task<AdminUserListItemDto> DeactivateUserAsync(Guid userId) =>
        (await PostAsync<object, AdminUserListItemDto>($"api/admin/users/{userId}/deactivate", new { }))!;

    public async Task<AdminUserListItemDto> ReactivateUserAsync(Guid userId) =>
        (await PostAsync<object, AdminUserListItemDto>($"api/admin/users/{userId}/reactivate", new { }))!;

    public async Task<List<OrganizationInviteListItemDto>> GetInvitesAsync() =>
        await GetAsync<List<OrganizationInviteListItemDto>>("api/admin/invites") ?? [];

    public async Task<List<InviteAuditNotificationDto>> GetInviteActivityAsync(int take = 25) =>
        await GetAsync<List<InviteAuditNotificationDto>>($"api/admin/invite-activity?take={Math.Clamp(take, 1, 100)}") ?? [];

    public Task MarkInviteActivityReadAllAsync() =>
        PostAsync("api/admin/invite-activity/mark-all-read", new { });

    public async Task<List<AdminAuditLogItemDto>> GetAuditLogsAsync(int take = 100, string? table = null, string? action = null)
    {
        var endpoint = $"api/admin/audit-logs?take={Math.Clamp(take, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(table))
            endpoint += $"&table={Uri.EscapeDataString(table)}";
        if (!string.IsNullOrWhiteSpace(action))
            endpoint += $"&action={Uri.EscapeDataString(action)}";

        return await GetAsync<List<AdminAuditLogItemDto>>(endpoint) ?? [];
    }

    public async Task<SeedDemoDataResponseDto> SeedDemoDataAsync() =>
        (await PostAsync<object, SeedDemoDataResponseDto>("api/admin/seed-demo-data", new { }))!;

    public async Task<CreateOrganizationInviteResponse> CreateInviteAsync(CreateOrganizationInviteRequest request) =>
        (await PostAsync<CreateOrganizationInviteRequest, CreateOrganizationInviteResponse>("api/admin/invites", request))!;

    public async Task<CreateOrganizationInviteResponse> ResendInviteAsync(Guid inviteId) =>
        (await PostAsync<object, CreateOrganizationInviteResponse>($"api/admin/invites/{inviteId}/resend", new { }))!;

    public Task RevokeInviteAsync(Guid inviteId) =>
        DeleteAsync($"api/admin/invites/{inviteId}");
}
