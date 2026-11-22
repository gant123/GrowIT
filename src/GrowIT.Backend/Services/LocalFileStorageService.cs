using GrowIT.Core.Interfaces;

namespace GrowIT.Backend.Services;

public class LocalFileStorageService : IFileStorageService
{
    private const string ProfilePhotoPrefix = "/uploads/profile-photos/";

    /// <summary>
    /// Configuration key for the private document store. Relative values resolve against the
    /// content root. Must NOT be inside wwwroot: documents are only served by the API.
    /// </summary>
    public const string DocumentsPathConfigKey = "Storage:DocumentsPath";
    public const string DefaultDocumentsPath = "App_Data/documents";

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<StoredFileResult> SaveDocumentAsync(
        Guid tenantId,
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant is required to store a document.", nameof(tenantId));

        var safeExtension = (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
        if (safeExtension.Length > 6 || safeExtension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Unsupported file extension.", nameof(extension));

        var root = GetDocumentsRoot();
        var tenantFolder = Path.Combine(root, tenantId.ToString("N"));
        Directory.CreateDirectory(tenantFolder);

        // Generated name only: the user's file name never touches the file system.
        var fileName = $"{Guid.NewGuid():N}{safeExtension}";
        var filePath = Path.Combine(tenantFolder, fileName);

        if (content.CanSeek)
            content.Position = 0;

        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await content.CopyToAsync(fileStream, cancellationToken);
        }
        catch
        {
            TryDeleteFile(filePath);
            throw;
        }

        return new StoredFileResult($"{tenantId:N}/{fileName}");
    }

    public Task<Stream?> OpenDocumentAsync(Guid tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var fullPath = ResolveDocumentPath(tenantId, storagePath);
        if (fullPath is null || !File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteDocumentAsync(Guid tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var fullPath = ResolveDocumentPath(tenantId, storagePath);
        if (fullPath is null)
        {
            _logger.LogWarning("Refusing to delete a document outside the tenant's document folder.");
            return Task.CompletedTask;
        }

        TryDeleteFile(fullPath);
        return Task.CompletedTask;
    }

    private string GetDocumentsRoot()
    {
        var configured = _configuration[DocumentsPathConfigKey];
        var root = string.IsNullOrWhiteSpace(configured) ? DefaultDocumentsPath : configured.Trim();
        if (!Path.IsPathRooted(root))
        {
            root = Path.Combine(_environment.ContentRootPath, root);
        }

        root = Path.GetFullPath(root);

        // Never let documents land somewhere the static-file middleware would serve them.
        var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;
        var webRootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(webRoot)) + Path.DirectorySeparatorChar;
        if ((Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar).StartsWith(webRootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{DocumentsPathConfigKey} must point outside the public web root (wwwroot); documents are private.");
        }

        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Maps a stored key to a file path, but only inside this tenant's folder. Returns null for
    /// anything else (another tenant's key, traversal attempts, absolute paths).
    /// </summary>
    private string? ResolveDocumentPath(Guid tenantId, string? storagePath)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(storagePath))
            return null;

        var normalized = storagePath.Trim().Replace('\\', '/');
        var tenantPrefix = tenantId.ToString("N") + "/";
        if (!normalized.StartsWith(tenantPrefix, StringComparison.Ordinal) || normalized.Contains(".."))
            return null;

        var root = GetDocumentsRoot();
        var tenantRoot = Path.GetFullPath(Path.Combine(root, tenantId.ToString("N"))) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));

        return fullPath.StartsWith(tenantRoot, StringComparison.Ordinal) ? fullPath : null;
    }

    private void TryDeleteFile(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete stored document file.");
        }
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

            // Defense-in-depth: the URL is normally server-generated, but never delete outside the
            // managed profile-photos root even if a stored/tampered path contains traversal ('..').
            var fullPath = Path.GetFullPath(localPath);
            var photosRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "profile-photos")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(photosRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Refusing to delete profile photo outside the managed root: {PhotoUrl}", existingPhotoUrl);
                return Task.CompletedTask;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
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
