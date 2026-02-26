using GrowIT.Core.Interfaces;

namespace GrowIT.Backend.Services;

public class LocalFileStorageService : IFileStorageService
{
    private const string ProfilePhotoPrefix = "/uploads/profile-photos/";
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(IWebHostEnvironment environment, ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<StoredFileResult> SaveProfilePhotoAsync(
        Guid tenantId,
        Guid userId,
        Stream content,
        string extension,
        string? existingPhotoUrl,
        CancellationToken cancellationToken = default)
    {
        var webRoot = EnsureWebRoot();
        var tenantFolder = Path.Combine(webRoot, "uploads", "profile-photos", tenantId.ToString("N"));
        Directory.CreateDirectory(tenantFolder);

        await DeleteProfilePhotoAsync(existingPhotoUrl, cancellationToken);

        var safeExtension = extension.StartsWith('.') ? extension : "." + extension;
        var fileName = $"{userId:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{safeExtension.ToLowerInvariant()}";
        var filePath = Path.Combine(tenantFolder, fileName);

        content.Position = 0;
        await using var fileStream = File.Create(filePath);
        await content.CopyToAsync(fileStream, cancellationToken);

        var relativePath = $"/uploads/profile-photos/{tenantId:N}/{fileName}";
        return new StoredFileResult(relativePath);
    }

    public Task DeleteProfilePhotoAsync(string? existingPhotoUrl, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(existingPhotoUrl))
            return Task.CompletedTask;

        try
        {
            var relativePath = TryGetManagedRelativePath(existingPhotoUrl);
            if (relativePath is null)
                return Task.CompletedTask;

            var webRoot = EnsureWebRoot();
            var localPath = Path.Combine(webRoot, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete managed profile photo {PhotoUrl}", existingPhotoUrl);
        }

        return Task.CompletedTask;
    }

    private string EnsureWebRoot()
    {
        var webRoot = _environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_environment.ContentRootPath, "wwwroot");
            Directory.CreateDirectory(webRoot);
            _environment.WebRootPath = webRoot;
        }

        return webRoot;
    }

    private static string? TryGetManagedRelativePath(string photoUrl)
    {
        if (photoUrl.StartsWith(ProfilePhotoPrefix, StringComparison.OrdinalIgnoreCase))
            return photoUrl;

        if (Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri) &&
            uri.AbsolutePath.StartsWith(ProfilePhotoPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath;
        }

        return null;
    }
}
