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

public class CreateDocumentRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    public string FileUrl { get; set; } = string.Empty;

    [Required]
    public string FileType { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
}

public class UpdateDocumentRequest : CreateDocumentRequest
{
}
