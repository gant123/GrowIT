namespace GrowIT.Shared.DTOs;

public class NotificationItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public string Category { get; set; } = "System";
    public DateTime CreatedAt { get; set; }
}

public class NotificationInboxResponseDto
{
    public List<NotificationItemDto> Items { get; set; } = [];
    public int UnreadCount { get; set; }
}
