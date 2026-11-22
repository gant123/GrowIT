namespace GrowIT.Core.Interfaces;

public sealed record StoredFileResult(string RelativePath);

public interface IFileStorageService
{
    Task<StoredFileResult> SaveProfilePhotoAsync(
        Guid tenantId,
        Guid userId,
        Stream content,
        string extension,
        string? existingPhotoUrl,
        CancellationToken cancellationToken = default);

    Task DeleteProfilePhotoAsync(string? existingPhotoUrl, CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------
    // Private documents (IDs, leases, IRS letters). Stored outside the public web root and only
    // ever served through the authenticated, tenant-checked download endpoint. The returned
    // RelativePath is an opaque storage key ("{tenant}/{generated}.ext"), not a URL.
    // ------------------------------------------------------------------

    Task<StoredFileResult> SaveDocumentAsync(
        Guid tenantId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored document for reading, or returns null when the key is unknown,
    /// belongs to a different tenant, or the file is missing.</summary>
    Task<Stream?> OpenDocumentAsync(Guid tenantId, string storagePath, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(Guid tenantId, string storagePath, CancellationToken cancellationToken = default);
}
