using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface INotificationService
{
    Task<NotificationInboxResponseDto> GetNotificationsAsync(int take = 25, bool unreadOnly = false);
    Task MarkAllReadAsync();
    Task MarkReadAsync(Guid id);
}

public class NotificationService : BaseApiService, INotificationService
{
    public NotificationService(HttpClient http) : base(http) { }

    public async Task<NotificationInboxResponseDto> GetNotificationsAsync(int take = 25, bool unreadOnly = false) =>
        (await GetAsync<NotificationInboxResponseDto>($"api/notifications?take={Math.Clamp(take, 1, 200)}&unreadOnly={unreadOnly.ToString().ToLowerInvariant()}"))!
        ?? new NotificationInboxResponseDto();

    public Task MarkAllReadAsync() =>
        PostAsync("api/notifications/mark-all-read", new { });

    public Task MarkReadAsync(Guid id) =>
        PostAsync($"api/notifications/{id}/mark-read", new { });
}
