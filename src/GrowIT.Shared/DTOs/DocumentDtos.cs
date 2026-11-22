using System.ComponentModel.DataAnnotations;
using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class DocumentDto
{
    public Guid Id { get; set; }

    /// <summary>Null for organization-level documents (IRS letter, bylaws, budget).</summary>
    public Guid? ClientId { get; set; }

    /// <summary>The person the document belongs to; null for organization documents.</summary>
    public string? ClientName { get; set; }

    /// <summary>Display name: the saved title, else the original file name, else the legacy link.</summary>
    public string Title { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
    public DocumentCategory Category { get; set; }

    /// <summary>True when a file is stored in grow.IT and can be downloaded.</summary>
    public bool IsUploaded { get; set; }

    /// <summary>Only for legacy rows saved as a typed web address before uploads existed.</summary>
    public string? LinkUrl { get; set; }
}

public enum DocumentScope
{
    All,
    Organization,
    People
}

public class DocumentQueryParams
{
    public Guid? ClientId { get; set; }

    public DocumentScope Scope { get; set; } = DocumentScope.All;

    [MaxLength(200)]
    public string? Search { get; set; }

    [Range(1, 100000)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;
}

/// <summary>
/// Metadata sent with an uploaded file (multipart form fields next to the "file" part).
/// </summary>
public class UploadDocumentRequest
{
    /// <summary>The person this document belongs to; leave empty for an organization document.</summary>
    public Guid? ClientId { get; set; }

    public DocumentCategory Category { get; set; } = DocumentCategory.Other;

    [MaxLength(200, ErrorMessage = "Keep the document name under 200 characters.")]
    public string? Title { get; set; }
}

/// <summary>
/// Upload rules shared by the API (enforcement) and the Documents page (early feedback).
/// </summary>
public static class DocumentUploadRules
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    // Multipart framing and the metadata fields add a little on top of the file itself.
    public const long MaxRequestBytes = MaxFileBytes + (512 * 1024);

    public const string MaxFileSizeLabel = "10 MB";

    /// <summary>Extension -> (content type served on download, short label shown in the list).</summary>
    public static readonly IReadOnlyDictionary<string, (string ContentType, string Label)> AllowedTypes =
        new Dictionary<string, (string ContentType, string Label)>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", "PDF"),
            [".png"] = ("image/png", "PNG"),
            [".jpg"] = ("image/jpeg", "JPG"),
            [".jpeg"] = ("image/jpeg", "JPG"),
            [".webp"] = ("image/webp", "WEBP"),
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "DOCX"),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "XLSX"),
            [".txt"] = ("text/plain", "TXT")
        };

    /// <summary>Value for an &lt;input type="file" accept="..."&gt; attribute.</summary>
    public static string AcceptAttribute => string.Join(",", AllowedTypes.Keys);

    public const string AllowedTypesLabel = "PDF, PNG, JPG, WEBP, Word (.docx), Excel (.xlsx) or plain text";

    public static bool IsAllowedExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && AllowedTypes.ContainsKey(extension);

    public static string FormatSize(long? bytes)
    {
        if (bytes is null or < 0)
        {
            return string.Empty;
        }

        return bytes.Value switch
        {
            < 1024 => $"{bytes.Value} B",
            < 1024 * 1024 => $"{bytes.Value / 1024d:0.#} KB",
            _ => $"{bytes.Value / (1024d * 1024d):0.#} MB"
        };
    }
}
