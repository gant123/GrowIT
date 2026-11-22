using System.Text;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    // Enough of the file to recognise its real format from the leading bytes.
    private const int SniffBytes = 8 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _storage;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IFileStorageService storage,
        ILogger<DocumentsController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _storage = storage;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetDocuments([FromQuery] DocumentQueryParams query, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        // Left join: organization documents have no person attached.
        var documentsQuery =
            from document in _context.Documents.Where(d => d.TenantId == tenantId.Value)
            join client in _context.Clients on document.ClientId equals (Guid?)client.Id into clients
            from client in clients.DefaultIfEmpty()
            select new { document, client };

        if (query.ClientId.HasValue)
        {
            documentsQuery = documentsQuery.Where(x => x.document.ClientId == query.ClientId.Value);
        }
        else if (query.Scope == DocumentScope.Organization)
        {
            documentsQuery = documentsQuery.Where(x => x.document.ClientId == null);
        }
        else if (query.Scope == DocumentScope.People)
        {
            documentsQuery = documentsQuery.Where(x => x.document.ClientId != null);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLower();
            documentsQuery = documentsQuery.Where(x =>
                (x.document.Title != null && x.document.Title.ToLower().Contains(search)) ||
                (x.document.OriginalFileName != null && x.document.OriginalFileName.ToLower().Contains(search)) ||
                x.document.FileType.ToLower().Contains(search) ||
                x.document.FileUrl.ToLower().Contains(search) ||
                (x.client != null && (x.client.FirstName + " " + x.client.LastName).ToLower().Contains(search)));
        }

        var documents = await documentsQuery
            .OrderByDescending(x => x.document.UploadedAt.HasValue)
            .ThenByDescending(x => x.document.UploadedAt)
            .ThenBy(x => x.document.Title ?? x.document.OriginalFileName ?? x.document.FileUrl)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new DocumentDto
            {
                Id = x.document.Id,
                ClientId = x.document.ClientId,
                ClientName = x.client == null ? null : x.client.FirstName + " " + x.client.LastName,
                Title = x.document.Title != null && x.document.Title != ""
                    ? x.document.Title
                    : x.document.OriginalFileName ?? x.document.FileUrl,
                OriginalFileName = x.document.OriginalFileName,
                FileType = x.document.FileType,
                ContentType = x.document.ContentType,
                SizeBytes = x.document.SizeBytes,
                UploadedAt = x.document.UploadedAt,
                Category = x.document.Category,
                IsUploaded = x.document.StoragePath != null,
                LinkUrl = x.document.StoragePath == null && x.document.FileUrl != "" ? x.document.FileUrl : null
            })
            .ToListAsync(cancellationToken);

        foreach (var document in documents)
        {
            if (document.UploadedAt.HasValue)
            {
                document.UploadedAt = DateTime.SpecifyKind(document.UploadedAt.Value, DateTimeKind.Utc);
            }
        }

        return Ok(documents);
    }

    /// <summary>
    /// Stores an uploaded file in the private document store (outside wwwroot) and records it.
    /// Leave clientId empty for an organization document (IRS letter, bylaws, budget).
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = "ServiceWriter")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(DocumentUploadRules.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentUploadRules.MaxRequestBytes)]
    public async Task<IActionResult> UploadDocument(
        [FromForm] UploadDocumentRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest("Choose a file to upload.");
        }

        if (file.Length > DocumentUploadRules.MaxFileBytes)
        {
            return BadRequest($"That file is too large. Files can be up to {DocumentUploadRules.MaxFileSizeLabel}.");
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty).ToLowerInvariant();
        if (!DocumentUploadRules.AllowedTypes.TryGetValue(extension, out var fileType))
        {
            return BadRequest($"That file type isn't supported. Upload a {DocumentUploadRules.AllowedTypesLabel} file.");
        }

        if (!Enum.IsDefined(request.Category))
        {
            return BadRequest("Choose a valid category.");
        }

        if (request.ClientId.HasValue)
        {
            if (request.ClientId.Value == Guid.Empty)
            {
                request.ClientId = null;
            }
            else
            {
                var clientExists = await _context.Clients
                    .AnyAsync(c => c.Id == request.ClientId.Value && c.TenantId == tenantId.Value, cancellationToken);
                if (!clientExists)
                {
                    return BadRequest("The selected person was not found.");
                }
            }
        }

        // Check the real format from the file's leading bytes: the extension and the browser's
        // content type are both user-controlled, so an HTML page renamed to .pdf is rejected.
        await using (var sniffStream = file.OpenReadStream())
        {
            var header = new byte[SniffBytes];
            var read = await sniffStream.ReadAtLeastAsync(header, SniffBytes, throwOnEndOfStream: false, cancellationToken);
            if (!MatchesDeclaredType(extension, header.AsSpan(0, read)))
            {
                return BadRequest($"That file doesn't look like a real {fileType.Label} file. Check the file and try again.");
            }
        }

        StoredFileResult stored;
        await using (var content = file.OpenReadStream())
        {
            stored = await _storage.SaveDocumentAsync(tenantId.Value, content, extension, cancellationToken);
        }

        var title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        var document = new Document
        {
            TenantId = tenantId.Value,
            ClientId = request.ClientId,
            Category = request.Category,
            Title = title,
            FileUrl = string.Empty,
            FileType = fileType.Label,
            StoragePath = stored.RelativePath,
            OriginalFileName = SanitizeOriginalFileName(file.FileName, extension),
            ContentType = fileType.ContentType,
            SizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = _currentUserService.UserId
        };

        try
        {
            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Don't leave an orphaned file behind when the record could not be saved.
            await _storage.DeleteDocumentAsync(tenantId.Value, stored.RelativePath, CancellationToken.None);
            throw;
        }

        return Ok(new EntityCreatedResponse
        {
            Message = "Document uploaded",
            Id = document.Id,
            ClientId = document.ClientId ?? Guid.Empty,
            DocumentId = document.Id
        });
    }

    /// <summary>
    /// Streams an uploaded document to an authenticated member of the owning organization.
    /// Always sent as an attachment with a sanitized file name and a server-chosen content type.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var document = await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId.Value, cancellationToken);
        if (document is null)
        {
            return NotFound("Document not found.");
        }

        if (string.IsNullOrWhiteSpace(document.StoragePath))
        {
            return NotFound("This document is a saved link, not an uploaded file.");
        }

        var extension = Path.GetExtension(document.StoragePath).ToLowerInvariant();
        if (!DocumentUploadRules.AllowedTypes.TryGetValue(extension, out var fileType))
        {
            return NotFound("The stored file could not be found.");
        }

        var stream = await _storage.OpenDocumentAsync(tenantId.Value, document.StoragePath, cancellationToken);
        if (stream is null)
        {
            _logger.LogWarning("Stored file for document {DocumentId} is missing.", document.Id);
            return NotFound("The stored file could not be found. Upload it again.");
        }

        Response.Headers.CacheControl = "no-store, private";
        return File(stream, fileType.ContentType, BuildDownloadFileName(document, extension));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId.Value, cancellationToken);
        if (document is null)
        {
            return NotFound("Document not found.");
        }

        var storagePath = document.StoragePath;
        _context.Documents.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            await _storage.DeleteDocumentAsync(tenantId.Value, storagePath, CancellationToken.None);
        }

        return NoContent();
    }

    internal static bool MatchesDeclaredType(string extension, ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty)
        {
            return false;
        }

        switch (extension)
        {
            case ".pdf":
                // "%PDF-" normally starts the file; tolerate a little leading junk as readers do.
                return header[..Math.Min(header.Length, 1024)].IndexOf("%PDF-"u8) >= 0;
            case ".png":
                return header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            case ".jpg":
            case ".jpeg":
                return header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF });
            case ".webp":
                return header.Length >= 12 && header.StartsWith("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8);
            case ".docx":
            case ".xlsx":
                // Office Open XML files are zip packages.
                return header.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            case ".txt":
                // Plain text: no NUL bytes (binary content) in the sampled prefix.
                return header.IndexOf((byte)0) < 0;
            default:
                return false;
        }
    }

    private static string SanitizeOriginalFileName(string? fileName, string extension)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "document" + extension;
        }

        return cleaned.Length <= 255 ? cleaned : cleaned[..(255 - extension.Length)] + extension;
    }

    /// <summary>
    /// A download name built from the user's title/file name but reduced to safe characters, so
    /// it can't break out of the Content-Disposition header or carry a path. The extension is
    /// always the stored (validated) one.
    /// </summary>
    internal static string BuildDownloadFileName(Document document, string extension)
    {
        var source = !string.IsNullOrWhiteSpace(document.Title)
            ? document.Title!
            : Path.GetFileNameWithoutExtension(document.OriginalFileName ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(document.Title) &&
            source.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^extension.Length];
        }

        var builder = new StringBuilder(source.Length);
        var lastWasSeparator = false;
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '(' or ')')
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append(ch is ' ' ? ' ' : '_');
                lastWasSeparator = true;
            }
        }

        var baseName = builder.ToString().Trim(' ', '_', '.');
        if (baseName.Length > 100)
        {
            baseName = baseName[..100].TrimEnd(' ', '_');
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = document.Category switch
            {
                DocumentCategory.ID => "id-document",
                _ => "document"
            };
        }

        return baseName + extension;
    }
}
