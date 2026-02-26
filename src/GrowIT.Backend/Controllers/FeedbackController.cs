using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public FeedbackController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    public async Task<ActionResult<BetaFeedbackListItemDto>> Submit([FromBody] CreateBetaFeedbackRequest request)
    {
        var tenantId = _tenantService.TenantId;
        var userId = _currentUserService.UserId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var now = DateTime.UtcNow;
        var item = new BetaFeedback
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.Value,
            UserId = userId.Value,
            Category = NormalizeCategory(request.Category),
            Severity = NormalizeSeverity(request.Severity),
            Title = request.Title.Trim(),
            Message = request.Message.Trim(),
            PageUrl = string.IsNullOrWhiteSpace(request.PageUrl) ? null : request.PageUrl.Trim(),
            Status = "Open",
            CreatedAt = now
        };

        _context.BetaFeedbacks.Add(item);
        await _context.SaveChangesAsync();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value);

        return Ok(Map(item, user));
    }

    [HttpGet("mine")]
    public async Task<ActionResult<List<BetaFeedbackListItemDto>>> GetMine([FromQuery] BetaFeedbackQueryParams query)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var items = await ApplyQuery(_context.BetaFeedbacks.AsNoTracking().Where(f => f.UserId == userId.Value), query)
            .OrderByDescending(f => f.CreatedAt)
            .Take(Math.Clamp(query.Take ?? 100, 1, 500))
            .ToListAsync();

        return Ok(items.Select(f => Map(f, null)).ToList());
    }

    private static IQueryable<BetaFeedback> ApplyQuery(IQueryable<BetaFeedback> source, BetaFeedbackQueryParams query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            source = source.Where(f =>
                f.Title.Contains(search) ||
                f.Message.Contains(search) ||
                (f.PageUrl ?? "").Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            source = source.Where(f => f.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            source = source.Where(f => f.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = query.Severity.Trim();
            source = source.Where(f => f.Severity == severity);
        }

        return source;
    }

    internal static string NormalizeCategory(string? value)
    {
        var normalized = (value ?? "Other").Trim();
        return normalized switch
        {
            "Bug" => "Bug",
            "UX" => "UX",
            "Feature Request" => "Feature Request",
            "Performance" => "Performance",
            "Data" => "Data",
            _ => "Other"
        };
    }

    internal static string NormalizeSeverity(string? value)
    {
        var normalized = (value ?? "Medium").Trim();
        return normalized switch
        {
            "Low" => "Low",
            "Medium" => "Medium",
            "High" => "High",
            "Critical" => "Critical",
            _ => "Medium"
        };
    }

    internal static BetaFeedbackListItemDto Map(BetaFeedback item, User? user)
    {
        return new BetaFeedbackListItemDto
        {
            Id = item.Id,
            Category = item.Category,
            Severity = item.Severity,
            Title = item.Title,
            Message = item.Message,
            PageUrl = item.PageUrl,
            Status = item.Status,
            AdminNotes = item.AdminNotes,
            UserId = item.UserId,
            SubmittedByName = user is null ? null : $"{user.FirstName} {user.LastName}".Trim(),
            SubmittedByEmail = user?.Email,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            ResolvedAt = item.ResolvedAt
        };
    }
}
