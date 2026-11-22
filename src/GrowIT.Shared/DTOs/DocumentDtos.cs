using GrowIT.Shared.Validation;
using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
}

public class DocumentQueryParams
{
    public Guid? ClientId { get; set; }

    [MaxLength(200)]
    public string? Search { get; set; }

    [Range(1, 100000)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;
}

public class CreateDocumentRequest
{
    [RequiredGuid(ErrorMessage = "Choose the person or household this document belongs to.")]
    public Guid ClientId { get; set; }

    [Required(ErrorMessage = "Add the link to the file.")]
    [Url(ErrorMessage = "The link must be a full web address starting with http:// or https://.")]
    [MaxLength(2000)]
    public string FileUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "Add the file type (for example PDF).")]
    [MaxLength(40)]
    public string FileType { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
}

public class UpdateDocumentRequest : CreateDocumentRequest
{
}
