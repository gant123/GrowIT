using System.Text;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[Authorize(Policy = "SuperAdminOnly")]
[ApiController]
[Route("api/admin/content")]
public class AdminContentController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AdminContentController(ApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    [HttpGet("blog")]
    public async Task<ActionResult<List<AdminBlogPostDto>>> GetBlogPosts([FromQuery] bool includeDrafts = true)
    {
        var query = _context.BlogPosts.AsNoTracking().AsQueryable();
        if (!includeDrafts)
        {
            query = query.Where(p => p.IsPublished);
        }

        var posts = await query
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .ToListAsync();

        return Ok(posts.Select(MapBlogPost).ToList());
    }

    [HttpPost("blog")]
    public async Task<ActionResult<AdminBlogPostDto>> CreateBlogPost([FromBody] CreateBlogPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Excerpt))
            return BadRequest("Excerpt is required.");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required.");

        var slug = await BuildUniqueSlugAsync(request.Slug, request.Title, null);
        var now = DateTime.UtcNow;
        var isPublished = request.PublishNow;

        var post = new BlogPost
        {
            Id = Guid.NewGuid(),
            Title = request.Title.Trim(),
            Slug = slug,
            Excerpt = request.Excerpt.Trim(),
            Content = request.Content.Trim(),
            Category = NormalizeCategory(request.Category),
            AuthorName = NormalizeAuthor(request.AuthorName),
            IsPublished = isPublished,
            PublishedAt = isPublished ? now : null,
            CreatedAt = now,
            CreatedByUserId = _currentUserService.UserId
        };

        _context.BlogPosts.Add(post);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBlogPosts), new { includeDrafts = true }, MapBlogPost(post));
    }

    [HttpPut("blog/{id:guid}")]
    public async Task<ActionResult<AdminBlogPostDto>> UpdateBlogPost(Guid id, [FromBody] UpdateBlogPostRequest request)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");
        if (string.IsNullOrWhiteSpace(request.Excerpt))
            return BadRequest("Excerpt is required.");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Content is required.");

        var now = DateTime.UtcNow;
        var slug = await BuildUniqueSlugAsync(request.Slug, request.Title, id);
        var wasPublished = post.IsPublished;

        post.Title = request.Title.Trim();
        post.Slug = slug;
        post.Excerpt = request.Excerpt.Trim();
        post.Content = request.Content.Trim();
        post.Category = NormalizeCategory(request.Category);
        post.AuthorName = NormalizeAuthor(request.AuthorName);
        post.IsPublished = request.IsPublished;
        post.UpdatedAt = now;
        post.UpdatedByUserId = _currentUserService.UserId;

        if (!wasPublished && request.IsPublished)
        {
            post.PublishedAt = now;
        }
        else if (wasPublished && !request.IsPublished)
        {
            post.PublishedAt = null;
        }

        await _context.SaveChangesAsync();
        return Ok(MapBlogPost(post));
    }

    [HttpDelete("blog/{id:guid}")]
    public async Task<IActionResult> DeleteBlogPost(Guid id)
    {
        var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return NotFound();

        _context.BlogPosts.Remove(post);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("contact")]
    public async Task<ActionResult<List<ContactSubmissionAdminDto>>> GetContactSubmissions([FromQuery] ContactSubmissionQueryParams query)
    {
        var take = Math.Clamp(query.Take ?? 300, 1, 1000);
        var source = _context.ContactSubmissions.AsNoTracking().AsQueryable();

        if (query.ReviewedOnly.HasValue)
        {
            source = source.Where(c => c.IsReviewed == query.ReviewedOnly.Value);
        }

        var submissions = await source
            .OrderByDescending(c => c.SubmittedAt)
            .Take(take)
            .Select(c => new ContactSubmissionAdminDto
            {
                Id = c.Id,
                FullName = c.FullName,
                Email = c.Email,
                Organization = c.Organization,
                Subject = c.Subject,
                Message = c.Message,
                SubmittedAt = c.SubmittedAt,
                IsReviewed = c.IsReviewed,
                ReviewedAt = c.ReviewedAt
            })
            .ToListAsync();

        return Ok(submissions);
    }

    [HttpPut("contact/{id:guid}/review")]
    public async Task<ActionResult<ContactSubmissionAdminDto>> UpdateContactSubmissionReviewStatus(
        Guid id,
        [FromBody] UpdateContactSubmissionReviewRequest request)
    {
        var item = await _context.ContactSubmissions.FirstOrDefaultAsync(c => c.Id == id);
        if (item is null)
            return NotFound();

        item.IsReviewed = request.IsReviewed;
        item.ReviewedAt = request.IsReviewed ? DateTime.UtcNow : null;
        item.ReviewedByUserId = request.IsReviewed ? _currentUserService.UserId : null;

        await _context.SaveChangesAsync();

        return Ok(new ContactSubmissionAdminDto
        {
            Id = item.Id,
            FullName = item.FullName,
            Email = item.Email,
            Organization = item.Organization,
            Subject = item.Subject,
            Message = item.Message,
            SubmittedAt = item.SubmittedAt,
            IsReviewed = item.IsReviewed,
            ReviewedAt = item.ReviewedAt
        });
    }

    [HttpGet("security-attempts")]
    public async Task<ActionResult<List<SecurityAccessAttemptDto>>> GetSecurityAttempts([FromQuery] SecurityAccessAttemptQueryParams query)
    {
        var take = Math.Clamp(query.Take ?? 250, 1, 1000);
        var source = _context.UnauthorizedAccessAttempts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Ip))
        {
            var ipFilter = query.Ip.Trim();
            source = source.Where(a => (a.ClientIp ?? string.Empty).Contains(ipFilter));
        }

        var attempts = await source
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .ToListAsync();

        var ipSet = attempts
            .Select(a => a.ClientIp?.Trim())
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var signIns = ipSet.Count == 0
            ? new List<UserSignInEvent>()
            : await _context.UserSignInEvents
                .AsNoTracking()
                .Where(e => e.ClientIp != null && ipSet.Contains(e.ClientIp))
                .OrderByDescending(e => e.OccurredAt)
                .ToListAsync();

        var userIds = signIns.Select(e => e.UserId).Distinct().ToList();
        var userMap = userIds.Count == 0
            ? new Dictionary<Guid, User>()
            : await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

        var result = attempts.Select(attempt =>
        {
            var matches = string.IsNullOrWhiteSpace(attempt.ClientIp)
                ? new List<SecurityKnownUserMatchDto>()
                : signIns
                    .Where(e => string.Equals(e.ClientIp, attempt.ClientIp, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(e => e.UserId)
                    .Select(g =>
                    {
                        var latest = g.OrderByDescending(x => x.OccurredAt).First();
                        userMap.TryGetValue(g.Key, out var user);
                        var fullName = user is null
                            ? (latest.DisplayName ?? latest.Email)
                            : $"{user.FirstName} {user.LastName}".Trim();
                        if (string.IsNullOrWhiteSpace(fullName))
                        {
                            fullName = latest.Email;
                        }

                        return new SecurityKnownUserMatchDto
                        {
                            UserId = g.Key,
                            Email = latest.Email,
                            FullName = fullName,
                            LastSeenAt = latest.OccurredAt
                        };
                    })
                    .OrderByDescending(m => m.LastSeenAt)
                    .ToList();

            return new SecurityAccessAttemptDto
            {
                Id = attempt.Id,
                Path = attempt.Path,
                ClientIp = attempt.ClientIp,
                UserAgent = attempt.UserAgent,
                Referer = attempt.Referer,
                OccurredAt = attempt.OccurredAt,
                IsAuthenticated = attempt.IsAuthenticated,
                UserId = attempt.UserId,
                KnownUsers = matches
            };
        }).ToList();

        return Ok(result);
    }

    private static AdminBlogPostDto MapBlogPost(BlogPost post) => new()
    {
        Id = post.Id,
        Title = post.Title,
        Slug = post.Slug,
        Excerpt = post.Excerpt,
        Content = post.Content,
        Category = post.Category,
        AuthorName = post.AuthorName,
        IsPublished = post.IsPublished,
        PublishedAt = post.PublishedAt,
        CreatedAt = post.CreatedAt,
        UpdatedAt = post.UpdatedAt
    };

    private async Task<string> BuildUniqueSlugAsync(string? requestedSlug, string title, Guid? existingId)
    {
        var baseSlug = ToSlug(string.IsNullOrWhiteSpace(requestedSlug) ? title : requestedSlug);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = $"post-{DateTime.UtcNow:yyyyMMddHHmmss}";
        }

        var slug = baseSlug;
        var suffix = 1;

        while (await _context.BlogPosts.AsNoTracking().AnyAsync(p =>
                   p.Slug == slug &&
                   (!existingId.HasValue || p.Id != existingId.Value)))
        {
            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }

        return slug;
    }

    private static string ToSlug(string value)
    {
        var input = value.Trim().ToLowerInvariant();
        var sb = new StringBuilder(input.Length);
        var prevDash = false;

        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        return sb.ToString().Trim('-');
    }

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();

    private static string NormalizeAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) ? "GrowIT Team" : author.Trim();
}
