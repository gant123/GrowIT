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
}
