using System.Net;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Core.Logging;
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
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<ContentController> _logger;

    public ContentController(
        ApplicationDbContext context,
        IEmailService emailService,
        IConfiguration config,
        ILogger<ContentController> logger)
    {
        _context = context;
        _emailService = emailService;
        _config = config;
        _logger = logger;
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

        await NotifyContactSubmissionAsync(submission);

        return Ok(new ContactSubmissionReceivedDto
        {
            Id = submission.Id,
            SubmittedAt = submission.SubmittedAt
        });
    }

    /// <summary>
    /// Emails the people listed in Contact:NotifyEmails (comma- or semicolon-separated) about a new
    /// contact-form message, so demo requests and other leads don't sit unread on the admin page.
    /// The submission is already saved; a send failure is logged and never fails the visitor's request.
    /// </summary>
    private async Task NotifyContactSubmissionAsync(ContactSubmission submission)
    {
        var recipients = ParseRecipients(_config["Contact:NotifyEmails"]);
        if (recipients.Count == 0)
        {
            return;
        }

        var subject = BuildNotificationSubject(submission);
        var body = BuildNotificationBody(submission, _config["ClientUrl"]);

        foreach (var recipient in recipients)
        {
            try
            {
                await _emailService.SendEmailAsync(recipient, subject, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to send contact-form notification for submission {SubmissionId} to {Recipient}.",
                    submission.Id, LogSanitizer.MaskEmail(recipient));
            }
        }
    }

    internal static List<string> ParseRecipients(string? configured) =>
        (configured ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static string BuildNotificationSubject(ContactSubmission submission)
    {
        var topic = string.IsNullOrWhiteSpace(submission.Subject) ? "Contact form message" : submission.Subject;
        var subject = $"New grow.IT contact: {topic} from {submission.FullName}";
        // Keep the subject a single line: visitors control these fields.
        return new string(subject.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
    }

    internal static string BuildNotificationBody(ContactSubmission submission, string? clientUrl)
    {
        static string Encode(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "&mdash;" : WebUtility.HtmlEncode(value);

        var adminUrl = $"{(string.IsNullOrWhiteSpace(clientUrl) ? "https://growit.now" : clientUrl.Trim().TrimEnd('/'))}/super-admin/content";
        var encodedEmail = WebUtility.HtmlEncode(submission.Email);
        var message = WebUtility.HtmlEncode(submission.Message)
            .Replace("\r\n", "\n")
            .Replace("\n", "<br/>");

        return $"""
            <h2>New contact form message</h2>
            <p>Someone sent a message through the grow.IT contact form.</p>
            <table cellpadding="4" style="border-collapse:collapse">
              <tr><td><strong>Name</strong></td><td>{Encode(submission.FullName)}</td></tr>
              <tr><td><strong>Email</strong></td><td><a href="mailto:{encodedEmail}">{encodedEmail}</a></td></tr>
              <tr><td><strong>Organization</strong></td><td>{Encode(submission.Organization)}</td></tr>
              <tr><td><strong>Subject</strong></td><td>{Encode(submission.Subject)}</td></tr>
              <tr><td><strong>Received</strong></td><td>{submission.SubmittedAt:yyyy-MM-dd HH:mm} UTC</td></tr>
            </table>
            <p><strong>Message</strong></p>
            <p>{message}</p>
            <p>Reply to the sender at the address above. All messages are listed on the
            <a href="{WebUtility.HtmlEncode(adminUrl)}">Site Content admin page</a>.</p>
            """;
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
