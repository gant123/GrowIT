using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Controllers;

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
    private readonly IWebHostEnvironment _environment;

    public ProfileController(ApplicationDbContext context, ICurrentUserService currentUserService, IWebHostEnvironment environment)
    {
        _context = context;
        _currentUserService = currentUserService;
        _environment = environment;
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

        return Ok(new UserProfileDto
        {
            UserId = user.Id,
            TenantId = user.TenantId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            PhotoUrl = user.PhotoUrl,
            NotifyInviteActivity = user.NotifyInviteActivity,
            NotifySystemAlerts = user.NotifySystemAlerts,
            CreatedAt = user.CreatedAt,
            OrganizationName = tenantName
        });
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

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            return BadRequest("New password must be at least 8 characters.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId.Value);
        if (user is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return BadRequest("Current password is incorrect.");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            return BadRequest("New password must be different from the current password.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.ResetTokenExpires = null;
        await _context.SaveChangesAsync();

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

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(webRoot);
        var uploadsRoot = Path.Combine(webRoot, "uploads", "profile-photos");
        var tenantFolder = Path.Combine(uploadsRoot, user.TenantId.ToString("N"));
        Directory.CreateDirectory(tenantFolder);

        DeleteExistingProfilePhotoIfManaged(user.PhotoUrl, webRoot);

        var fileName = $"{user.Id:N}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        var filePath = Path.Combine(tenantFolder, fileName);

        await using (var stream = System.IO.File.Create(filePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = $"/uploads/profile-photos/{user.TenantId:N}/{fileName}";
        user.PhotoUrl = $"{Request.Scheme}://{Request.Host}{relativePath}";
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

    private static void DeleteExistingProfilePhotoIfManaged(string? currentPhotoUrl, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(currentPhotoUrl))
            return;

        try
        {
            if (!Uri.TryCreate(currentPhotoUrl, UriKind.Absolute, out var uri))
                return;

            var absolutePath = uri.AbsolutePath;
            if (!absolutePath.StartsWith("/uploads/profile-photos/", StringComparison.OrdinalIgnoreCase))
                return;

            var relativePath = absolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(webRoot, relativePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
        catch
        {
            // Ignore cleanup issues to avoid blocking uploads.
        }
    }
}
