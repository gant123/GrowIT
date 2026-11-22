using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Route("api/content")]
public class ContentController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ContentController(ApplicationDbContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("blog")]
    public async Task<ActionResult<List<PublicBlogPostDto>>> GetPublishedBlogPosts([FromQuery] int take = 24)
    {
        take = Math.Clamp(take, 1, 100);

        var posts = await _context.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Take(take)
            .Select(p => new PublicBlogPostDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                Content = p.Content,
                Category = p.Category,
                AuthorName = p.AuthorName,
                PublishedAt = p.PublishedAt ?? p.CreatedAt
            })
            .ToListAsync();

        foreach (var post in posts)
        {
            post.Slug = NormalizeSlug(post.Slug);
        }

        return Ok(posts);
    }

    /// <summary>
    /// One published article for the public /blog/{slug} page. Drafts and unknown slugs are 404.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("blog/{slug}")]
    public async Task<ActionResult<PublicBlogPostDto>> GetPublishedBlogPost(string slug)
    {
        var normalized = NormalizeSlug(slug);
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 200)
        {
            return NotFound("That article isn't available.");
        }

        // Older rows may have been saved with a leading slash; match those too.
        var legacySlug = "/" + normalized;
        var post = await _context.BlogPosts
            .AsNoTracking()
            .Where(p => p.IsPublished && (p.Slug == normalized || p.Slug == legacySlug))
            .Select(p => new PublicBlogPostDto
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                Content = p.Content,
                Category = p.Category,
                AuthorName = p.AuthorName,
                PublishedAt = p.PublishedAt ?? p.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (post is null)
        {
            return NotFound("That article isn't available.");
        }

        post.Slug = NormalizeSlug(post.Slug);
        return Ok(post);
    }

    internal static string NormalizeSlug(string? slug) =>
        (slug ?? string.Empty).Trim().Trim('/').Trim().ToLowerInvariant();

    [AllowAnonymous]
    [EnableRateLimiting("contact-submit")]
    [HttpPost("contact")]
    public async Task<ActionResult<ContactSubmissionReceivedDto>> SubmitContact([FromBody] CreateContactSubmissionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest("Full name is required.");
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required.");
        if (!IsValidEmail(request.Email))
            return BadRequest("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");

        var now = DateTime.UtcNow;
        var submission = new ContactSubmission
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Organization = string.IsNullOrWhiteSpace(request.Organization) ? null : request.Organization.Trim(),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
            Message = request.Message.Trim(),
            SubmittedAt = now
        };

        _context.ContactSubmissions.Add(submission);
        await _context.SaveChangesAsync();

        return Ok(new ContactSubmissionReceivedDto
        {
            Id = submission.Id,
            SubmittedAt = submission.SubmittedAt
        });
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var address = new System.Net.Mail.MailAddress(email.Trim());
            return !string.IsNullOrWhiteSpace(address.Address);
        }
        catch
        {
            return false;
        }
    }
}
