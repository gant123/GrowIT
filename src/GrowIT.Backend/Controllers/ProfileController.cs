using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFileStorageService _fileStorage;
    private readonly UserManager<GrowIT.Core.Entities.User> _userManager;

    public ProfileController(
        ApplicationDbContext context,
        ICurrentUserService currentUserService,
        IFileStorageService fileStorage,
        UserManager<GrowIT.Core.Entities.User> userManager)
    {
        _context = context;
        _currentUserService = currentUserService;
        _fileStorage = fileStorage;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        var tenantName = await _context.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync();

        return Ok(ToProfileDto(user, tenantName));
    }

    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(UpdateUserProfileRequest request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest("First name and last name are required.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        await _context.SaveChangesAsync();

        return await GetProfile();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return BadRequest("Current password is required.");

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12)
            return BadRequest("New password must be at least 12 characters.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        if (await _userManager.CheckPasswordAsync(user, request.NewPassword))
            return BadRequest("New password must be different from the current password.");

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(new { Message = "Password updated successfully." });
    }

    [HttpPut("notification-preferences")]
    public async Task<ActionResult<UserProfileDto>> UpdateNotificationPreferences(UpdateNotificationPreferencesRequest request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        user.NotifyInviteActivity = request.NotifyInviteActivity;
        user.NotifySystemAlerts = request.NotifySystemAlerts;
        await _context.SaveChangesAsync();

        return await GetProfile();
    }

    [HttpPost("photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<ActionResult<UserProfileDto>> UploadPhoto([FromForm] IFormFile? file)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        if (file is null || file.Length == 0)
            return BadRequest("Please provide an image file.");

        if (file.Length > MaxPhotoBytes)
            return BadRequest("Profile photo must be 5MB or smaller.");

        if (!AllowedImageContentTypes.Contains(file.ContentType))
            return BadRequest("Only JPG, PNG, and WEBP images are allowed.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        var extension = GetImageExtension(file.ContentType, file.FileName);
        if (extension is null)
            return BadRequest("Unsupported image file type.");

        await using var stream = file.OpenReadStream();
        var stored = await _fileStorage.SaveProfilePhotoAsync(
            user.TenantId,
            user.Id,
            stream,
            extension,
            user.PhotoUrl,
            HttpContext.RequestAborted);

        user.PhotoUrl = stored.RelativePath;
        await _context.SaveChangesAsync();

        return await GetProfile();
    }

    [HttpDelete("photo")]
    public async Task<ActionResult<UserProfileDto>> RemovePhoto()
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue || userId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        await _fileStorage.DeleteProfilePhotoAsync(user.PhotoUrl, HttpContext.RequestAborted);
        user.PhotoUrl = null;
        await _context.SaveChangesAsync();

        return await GetProfile();
    }

    private static string? GetImageExtension(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
            if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
        }

        var ext = Path.GetExtension(fileName);
        return ext?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ".jpg",
            ".png" => ".png",
            ".webp" => ".webp",
            _ => null
        };
    }

    private UserProfileDto ToProfileDto(GrowIT.Core.Entities.User user, string? tenantName)
    {
        return new UserProfileDto
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email ?? string.Empty,
            Role = user.Role,
            IsActive = user.IsActive,
            PhotoUrl = ToPublicPhotoUrl(user.PhotoUrl),
            NotifyInviteActivity = user.NotifyInviteActivity,
            NotifySystemAlerts = user.NotifySystemAlerts,
            CreatedAt = user.CreatedAt,
            OrganizationName = tenantName
        };
    }

    private string? ToPublicPhotoUrl(string? storedPhotoUrl)
    {
        if (string.IsNullOrWhiteSpace(storedPhotoUrl))
            return null;

        if (Uri.TryCreate(storedPhotoUrl, UriKind.Absolute, out var absolute))
        {
            // For locally-managed uploads, return a relative path so the current host
            // (localhost/LAN/tunnel domain) always serves the image correctly.
            if (absolute.AbsolutePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                return absolute.PathAndQuery;
            }

            return storedPhotoUrl;
        }

        if (!storedPhotoUrl.StartsWith('/'))
            storedPhotoUrl = "/" + storedPhotoUrl;

        return storedPhotoUrl;
    }
}
