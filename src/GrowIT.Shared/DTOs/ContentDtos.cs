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
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string AuthorName { get; set; } = "GrowIT Team";
    public bool PublishNow { get; set; }
}

public class UpdateBlogPostRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string Excerpt { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string AuthorName { get; set; } = "GrowIT Team";
    public bool IsPublished { get; set; }
}

public class CreateContactSubmissionRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Organization { get; set; }
    public string? Subject { get; set; }
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
