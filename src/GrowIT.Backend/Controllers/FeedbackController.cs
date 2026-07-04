using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FeedbackController : ControllerBase
{
    private static readonly TimeSpan DuplicateFeedbackWindow = TimeSpan.FromMinutes(10);

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
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var now = DateTime.UtcNow;
        var category = NormalizeCategory(request.Category);
        var severity = NormalizeSeverity(request.Severity);
        var title = request.Title.Trim();
        var message = request.Message.Trim();
        var pageUrl = string.IsNullOrWhiteSpace(request.PageUrl) ? null : request.PageUrl.Trim();
        var fingerprint = BuildSubmissionFingerprint(category, severity, title, message, pageUrl);
        var recentSince = now.Subtract(DuplicateFeedbackWindow);

        var existing = await _context.BetaFeedbacks
            .AsNoTracking()
            .Where(f =>
                f.UserId == userId.Value &&
                f.SubmissionFingerprint == fingerprint &&
                f.CreatedAt >= recentSince)
            .OrderByDescending(f => f.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            return Ok(await MapWithLookupsAsync(existing, userId.Value));
        }

        var item = new BetaFeedback
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId.HasValue && tenantId.Value != Guid.Empty ? tenantId.Value : null,
            UserId = userId.Value,
            Category = category,
            Severity = severity,
            Title = title,
            Message = message,
            PageUrl = pageUrl,
            SubmissionFingerprint = fingerprint,
            IdempotencyKey = BuildIdempotencyKey("feedback", tenantId, userId.Value, fingerprint, now, DuplicateFeedbackWindow),
            Status = "Open",
            CreatedAt = now
        };

        _context.BetaFeedbacks.Add(item);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var duplicate = await _context.BetaFeedbacks
                .AsNoTracking()
                .Where(f =>
                    f.UserId == userId.Value &&
                    f.SubmissionFingerprint == fingerprint &&
                    f.CreatedAt >= recentSince)
                .OrderByDescending(f => f.CreatedAt)
                .FirstOrDefaultAsync();

            if (duplicate is null)
            {
                throw;
            }

            return Ok(await MapWithLookupsAsync(duplicate, userId.Value));
        }

        return Ok(await MapWithLookupsAsync(item, userId.Value));
    }

    private async Task<BetaFeedbackListItemDto> MapWithLookupsAsync(BetaFeedback item, Guid userId)
    {
        // Ignore the tenant query filter: BetaFeedback is platform-owned and a SuperAdmin may
        // submit without a tenant context, which would otherwise return null for their own record.
        var user = await _context.Users.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        var tenant = item.TenantId.HasValue
            ? await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == item.TenantId.Value)
            : null;

        return Map(item, user, tenant);
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

        return Ok(items.Select(f => Map(f, null, null)).ToList());
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

    private static string BuildSubmissionFingerprint(string category, string severity, string title, string message, string? pageUrl)
    {
        var normalized = string.Join("|",
            category.Trim().ToUpperInvariant(),
            severity.Trim().ToUpperInvariant(),
            NormalizeForFingerprint(title),
            NormalizeForFingerprint(message),
            NormalizeForFingerprint(pageUrl ?? string.Empty));

        return Sha256Hex(normalized);
    }

    private static string BuildIdempotencyKey(string scope, Guid? tenantId, Guid userId, string fingerprint, DateTime now, TimeSpan window)
    {
        var bucketTicks = now.Ticks - (now.Ticks % window.Ticks);
        return Sha256Hex($"{scope}|{tenantId?.ToString() ?? "platform"}|{userId}|{fingerprint}|{bucketTicks}");
    }

    private static string NormalizeForFingerprint(string value)
    {
        return string.Join(' ', value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static BetaFeedbackListItemDto Map(BetaFeedback item, User? user, Tenant? tenant)
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
            TenantId = item.TenantId,
            TenantName = tenant?.Name,
            UserId = item.UserId,
            SubmittedByName = user is null ? null : $"{user.FirstName} {user.LastName}".Trim(),
            SubmittedByEmail = user?.Email,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            ResolvedAt = item.ResolvedAt
        };
    }
}
