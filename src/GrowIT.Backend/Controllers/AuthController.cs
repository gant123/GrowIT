using GrowIT.Shared.DTOs;
using GrowIT.Backend.Services;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.Enums;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Web;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string InviteAuditLink = "/settings?tab=invites";
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public AuthController(
        ApplicationDbContext context, 
        TokenService tokenService, 
        IEmailService emailService,
        IConfiguration config,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        // 0. Check if user already exists
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var existingUser = await _userManager.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail);
            
        if (existingUser)
        {
            return BadRequest(new { Message = "User with this email already exists." });
        }

        // 1. Create the Tenant (Organization)
        var newTenant = new Tenant
        {
            Name = request.OrganizationName,
            // Defaults
            SubscriptionPlan = SubscriptionPlanType.Free,
            OrganizationType = request.OrganizationType,
            OrganizationSize = request.OrganizationSize,
            TrackPeople = request.TrackPeople,
            TrackInvestments = request.TrackInvestments,
            TrackOutcomes = request.TrackOutcomes,
            TrackPrograms = request.TrackPrograms
        };

        // 2. Create the User (Admin)
        var newUser = new User
        {
            TenantId = newTenant.Id, // Link to the new Tenant
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            Role = "Admin",
            IsActive = true,
            NotifyInviteActivity = true,
            NotifySystemAlerts = true,
            EmailConfirmed = true
        };

        // 3. Save as a Transaction (All or Nothing)
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try 
        {
            _context.Tenants.Add(newTenant);
            await _context.SaveChangesAsync();

            var createResult = await _userManager.CreateAsync(newUser, request.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return BadRequest(new
                {
                    Message = "Registration failed.",
                    Errors = createResult.Errors.Select(e => e.Description).ToArray()
                });
            }

            await EnsureRoleAsync("Admin");
            await _userManager.AddToRoleAsync(newUser, "Admin");
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return BadRequest(new { Message = "Registration failed.", Error = ex.Message, InnerError = ex.InnerException?.Message });
        }

        return Ok(new { Message = "Organization Registered!", TenantId = newTenant.Id });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        // 1. Find User
        // Use IgnoreQueryFilters() to find user across all tenants during login
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            
        if (user == null) 
        {
            // Security Best Practice: Don't reveal if user exists
            return Unauthorized("Invalid email or password");
        }

        // 2. Check Password
        if (!user.IsActive)
        {
            return Unauthorized("This account has been deactivated. Contact your organization administrator.");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (signInResult.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, "This account is temporarily locked due to repeated failed sign-in attempts.");
        }

        if (!signInResult.Succeeded)
        {
            return Unauthorized("Invalid email or password");
        }

        // 3. Generate Token
        // Important: Use user.TenantId to ensure the token has the correct tenant context
        var token = await _tokenService.CreateTokenAsync(user, user.TenantId);

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.Id,
            TenantId = user.TenantId
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email.Trim());

        if (user == null)
        {
            // Security: Don't reveal if user exists
            return Ok(new { Message = "If your email is in our system, you will receive a reset link." });
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Send email
        var clientUrl = _config["ClientUrl"] ?? "https://localhost:7234";
        var resetLink = $"{clientUrl}/reset-password?token={HttpUtility.UrlEncode(token)}&email={HttpUtility.UrlEncode(user.Email)}";
        
        var body = $@"
            <h1>Reset your password</h1>
            <p>You requested a password reset for GrowIT.</p>
            <p>Please click the link below to reset your password. This link will expire in 2 hours.</p>
            <p><a href='{resetLink}'>{resetLink}</a></p>
            <p>If you did not request this, please ignore this email.</p>";

        try
        {
            await _emailService.SendEmailAsync(user.Email ?? request.Email.Trim(), "Reset your GrowIT password", body);
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the request if email sending fails
            // This prevents leaking user existence and provides a better UX
            // In a real app, you might want to retry or use a queue
            Console.WriteLine($"[ERROR] Failed to send password reset email: {ex.Message}");
        }

        return Ok(new { Message = "If your email is in our system, you will receive a reset link." });
    }

    [HttpGet("invites/validate")]
    public async Task<ActionResult<InviteValidationDto>> ValidateInvite([FromQuery] string token, [FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new InviteValidationDto { IsValid = false, Message = "Invite token and email are required." });
        }

        var tokenHash = HashToken(token);
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var invite = await _context.OrganizationInvites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash && i.Email.ToLower() == normalizedEmail);

        if (invite is null)
        {
            return NotFound(new InviteValidationDto { IsValid = false, Message = "Invite not found." });
        }

        if (invite.RevokedAt != null)
        {
            return BadRequest(new InviteValidationDto { IsValid = false, Message = "This invite has been revoked." });
        }

        if (invite.AcceptedAt != null)
        {
            return BadRequest(new InviteValidationDto { IsValid = false, Message = "This invite has already been accepted." });
        }

        if (invite.ExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new InviteValidationDto { IsValid = false, Message = "This invite has expired." });
        }

        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == invite.TenantId);

        return Ok(new InviteValidationDto
        {
            IsValid = true,
            Message = "Invite is valid.",
            Email = invite.Email,
            OrganizationName = tenant?.Name ?? "Organization",
            Role = invite.Role,
            ExpiresAt = invite.ExpiresAt
        });
    }

    [HttpPost("accept-invite")]
    public async Task<ActionResult<AuthResponseDto>> AcceptInvite(AcceptInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Invite token and email are required.");

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12)
            return BadRequest("Password must be at least 12 characters.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var tokenHash = HashToken(request.Token);

        var invite = await _context.OrganizationInvites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash && i.Email.ToLower() == normalizedEmail);

        if (invite is null) return BadRequest("Invalid invite.");
        if (invite.RevokedAt != null) return BadRequest("Invite has been revoked.");
        if (invite.AcceptedAt != null) return BadRequest("Invite has already been accepted.");
        if (invite.ExpiresAt < DateTime.UtcNow) return BadRequest("Invite has expired.");

        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        if (existingUser)
            return BadRequest("A user with this email already exists.");

        var user = new User
        {
            TenantId = invite.TenantId,
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? invite.FirstName : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? invite.LastName : request.LastName.Trim(),
            Email = normalizedEmail,
            UserName = normalizedEmail,
            Role = string.IsNullOrWhiteSpace(invite.Role) ? "Member" : invite.Role,
            IsActive = true,
            NotifyInviteActivity = true,
            NotifySystemAlerts = true,
            EmailConfirmed = true
        };

        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
            return BadRequest("First name and last name are required.");

        invite.AcceptedAt = DateTime.UtcNow;
        invite.AcceptedUserId = user.Id;
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(string.Join(" ", createResult.Errors.Select(e => e.Description)));
        }

        var assignedRole = string.IsNullOrWhiteSpace(user.Role) ? "Member" : user.Role;
        await EnsureRoleAsync(assignedRole);
        await _userManager.AddToRoleAsync(user, assignedRole);
        await AddInviteAcceptedNotificationsAsync(invite, user);
        await _context.SaveChangesAsync();

        var token = await _tokenService.CreateTokenAsync(user, user.TenantId);

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.Id,
            TenantId = user.TenantId
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email.Trim());
        if (user == null)
        {
            return BadRequest(new { Message = "Invalid or expired token." });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { Message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(new { Message = "Password has been reset successfully." });
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private async Task AddInviteAcceptedNotificationsAsync(OrganizationInvite invite, User acceptedUser)
    {
        var recipientIds = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.TenantId == invite.TenantId && u.IsActive && u.NotifyInviteActivity)
            .Select(u => u.Id)
            .ToListAsync();

        if (recipientIds.Count == 0)
            return;

        var displayName = string.Join(" ", new[] { acceptedUser.FirstName, acceptedUser.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = acceptedUser.Email;

        var now = DateTime.UtcNow;
        foreach (var userId in recipientIds)
        {
            _context.Notifications.Add(new Notification
            {
                TenantId = invite.TenantId,
                UserId = userId,
                Title = "Invite accepted",
                Message = $"{displayName} joined the organization as {acceptedUser.Role}.",
                Link = InviteAuditLink,
                IsRead = false,
                CreatedAt = now
            });
        }
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        var normalizedRole = roleName.Trim();
        if (!await _roleManager.RoleExistsAsync(normalizedRole))
        {
            var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(normalizedRole));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create role '{normalizedRole}': {string.Join(" ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}
