using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class PublicBlogPostDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

public class AdminBlogPostDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateBlogPostRequest
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(200)]
    public string? Slug { get; set; }
    [StringLength(500)]
    public string Excerpt { get; set; } = string.Empty;
    [StringLength(50000)]
    public string Content { get; set; } = string.Empty;
    [StringLength(80)]
    public string Category { get; set; } = "General";
    [StringLength(120)]
    public string AuthorName { get; set; } = "GrowIT Team";
    public bool PublishNow { get; set; }
}

public class UpdateBlogPostRequest
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(200)]
    public string? Slug { get; set; }
    [StringLength(500)]
    public string Excerpt { get; set; } = string.Empty;
    [StringLength(50000)]
    public string Content { get; set; } = string.Empty;
    [StringLength(80)]
    public string Category { get; set; } = "General";
    [StringLength(120)]
    public string AuthorName { get; set; } = "GrowIT Team";
    public bool IsPublished { get; set; }
}

public class CreateContactSubmissionRequest
{
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(120, ErrorMessage = "Your name can be up to 120 characters.")]
    public string FullName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Please enter your email address.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(160, ErrorMessage = "Your email can be up to 160 characters.")]
    public string Email { get; set; } = string.Empty;
    [StringLength(160, ErrorMessage = "Organization can be up to 160 characters.")]
    public string? Organization { get; set; }
    [StringLength(180, ErrorMessage = "Subject can be up to 180 characters.")]
    public string? Subject { get; set; }
    [Required(ErrorMessage = "Please enter a message.")]
    [StringLength(3000, ErrorMessage = "Your message can be up to 3,000 characters.")]
    public string Message { get; set; } = string.Empty;
}

public class ContactSubmissionReceivedDto
{
    public Guid Id { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class ContactSubmissionAdminDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Subject { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public bool IsReviewed { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class ContactSubmissionQueryParams
{
    public bool? ReviewedOnly { get; set; }
    public int? Take { get; set; }
}

public class UpdateContactSubmissionReviewRequest
{
    public bool IsReviewed { get; set; } = true;
}
