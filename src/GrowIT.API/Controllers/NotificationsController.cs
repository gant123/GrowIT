using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public NotificationsController(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationInboxResponseDto>> GetNotifications([FromQuery] int take = 25, [FromQuery] bool unreadOnly = false)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        take = Math.Clamp(take, 1, 200);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null)
            return NotFound();

        var baseQuery = _context.Notifications.Where(n => n.UserId == userId.Value);

        if (!user.NotifyInviteActivity)
        {
            baseQuery = baseQuery.Where(n => n.Link == null || !n.Link.Contains("tab=invites"));
        }

        if (!user.NotifySystemAlerts)
        {
            baseQuery = baseQuery.Where(n => n.Link != null && n.Link.Contains("tab=invites"));
        }

        var unreadCount = await baseQuery.CountAsync(n => !n.IsRead);

        if (unreadOnly)
        {
            baseQuery = baseQuery.Where(n => !n.IsRead);
        }

        var items = await baseQuery
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationItemDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Link = n.Link,
                IsRead = n.IsRead,
                Category = n.Link != null && n.Link.Contains("tab=invites") ? "Invite Activity" : "System",
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return Ok(new NotificationInboxResponseDto
        {
            Items = items,
            UnreadCount = unreadCount
        });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var unread = await _context.Notifications
            .Where(n => n.UserId == userId.Value && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }

        if (unread.Count > 0)
        {
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/mark-read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId.Value);

        if (notification is null) return NotFound();
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }
}
