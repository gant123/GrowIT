using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Controllers;

[Authorize(Policy = "AdminOrManager")]
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

        var users = _context.Users.AsNoTracking();
        var items = await feedbackQuery
            .OrderByDescending(f => f.CreatedAt)
            .Take(take)
            .GroupJoin(users, f => f.UserId, u => u.Id, (f, joined) => new { f, u = joined.FirstOrDefault() })
            .Select(x => new BetaFeedbackListItemDto
            {
                Id = x.f.Id,
                Category = x.f.Category,
                Severity = x.f.Severity,
                Title = x.f.Title,
                Message = x.f.Message,
                PageUrl = x.f.PageUrl,
                Status = x.f.Status,
                AdminNotes = x.f.AdminNotes,
                UserId = x.f.UserId,
                SubmittedByName = x.u == null ? null : (x.u.FirstName + " " + x.u.LastName).Trim(),
                SubmittedByEmail = x.u == null ? null : x.u.Email,
                CreatedAt = x.f.CreatedAt,
                UpdatedAt = x.f.UpdatedAt,
                ResolvedAt = x.f.ResolvedAt
            })
            .ToListAsync();

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

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == item.UserId);
        return Ok(FeedbackController.Map(item, user));
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
