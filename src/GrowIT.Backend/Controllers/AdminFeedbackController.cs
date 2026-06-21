using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[Authorize(Policy = "SuperAdminOnly")]
[ApiController]
[Route("api/admin/feedback")]
public class AdminFeedbackController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AdminFeedbackController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<BetaFeedbackListItemDto>>> Get([FromQuery] BetaFeedbackQueryParams query)
    {
        var feedbackQuery = _context.BetaFeedbacks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            feedbackQuery = feedbackQuery.Where(f =>
                f.Title.Contains(search) ||
                f.Message.Contains(search) ||
                (f.PageUrl ?? "").Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            feedbackQuery = feedbackQuery.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            feedbackQuery = feedbackQuery.Where(f => f.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = query.Severity.Trim();
            feedbackQuery = feedbackQuery.Where(f => f.Severity == severity);
        }

        var take = Math.Clamp(query.Take ?? 200, 1, 1000);

        var feedback = await feedbackQuery
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .ToListAsync();

        var userIds = feedback.Where(f => f.UserId.HasValue).Select(f => f.UserId!.Value).Distinct().ToList();
        var tenantIds = feedback.Where(f => f.TenantId.HasValue).Select(f => f.TenantId!.Value).Distinct().ToList();

        var users = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        var tenants = await _context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id);

        var items = feedback
            .Select(f =>
            {
                var user = f.UserId.HasValue && users.TryGetValue(f.UserId.Value, out var matchedUser)
                    ? matchedUser
                    : null;
                var tenant = f.TenantId.HasValue && tenants.TryGetValue(f.TenantId.Value, out var matchedTenant)
                    ? matchedTenant
                    : null;
                return FeedbackController.Map(f, user, tenant);
            })
            .ToList();

        return Ok(items);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<BetaFeedbackListItemDto>> UpdateStatus(Guid id, [FromBody] UpdateBetaFeedbackStatusRequest request)
    {
        var item = await _context.BetaFeedbacks.FirstOrDefaultAsync(f => f.Id == id);
        if (item is null)
            return NotFound();

        var normalizedStatus = NormalizeStatus(request.Status);
        item.Status = normalizedStatus;
        item.AdminNotes = string.IsNullOrWhiteSpace(request.AdminNotes) ? null : request.AdminNotes.Trim();
        item.UpdatedAt = DateTime.UtcNow;
        item.ResolvedAt = normalizedStatus is "Resolved" or "Dismissed" ? DateTime.UtcNow : null;

        await _context.SaveChangesAsync();

        var user = item.UserId.HasValue
            ? await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == item.UserId.Value)
            : null;
        var tenant = item.TenantId.HasValue
            ? await _context.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == item.TenantId.Value)
            : null;
        return Ok(FeedbackController.Map(item, user, tenant));
    }

    private static string NormalizeStatus(string? value)
    {
        var normalized = (value ?? "Open").Trim();
        return normalized switch
        {
            "Open" => "Open",
            "In Review" => "In Review",
            "Planned" => "Planned",
            "Resolved" => "Resolved",
            "Dismissed" => "Dismissed",
            _ => "Open"
        };
    }
}
