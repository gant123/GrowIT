using GrowIT.Shared.DTOs;
using GrowIT.Backend.Services;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.Enums;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Web;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string InviteAuditLink = "/settings?tab=invites";
    private static readonly TimeSpan ConfirmationEmailCooldown = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PasswordResetEmailCooldown = TimeSpan.FromMinutes(10);
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext context, 
        TokenService tokenService, 
        IEmailService emailService,
        IConfiguration config,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<AuthController> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _logger = logger;
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
            return BadRequest(new MessageResponse { Message = "User with this email already exists." });
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
            IsActive = true,
            NotifyInviteActivity = true,
            NotifySystemAlerts = true,
            EmailConfirmed = false
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
            var addToRoleResult = await _userManager.AddToRoleAsync(newUser, "Admin");
            if (!addToRoleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return BadRequest(new
                {
                    Message = "Registration failed.",
                    Errors = addToRoleResult.Errors.Select(e => e.Description).ToArray()
                });
            }
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Registration failed for {Email}.", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError, new MessageResponse { Message = "Registration failed. Please try again." });
        }

        try
        {
            await SendEmailConfirmationAsync(newUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email for {Email}.", newUser.Email);
            return Accepted(new RegisterResponseDto
            {
                Message = "Organization created. We could not deliver the confirmation email yet. Use resend confirmation from sign in.",
                TenantId = newTenant.Id,
                RequiresEmailConfirmation = true,
                Email = newUser.Email ?? string.Empty
            });
        }

        return Ok(new RegisterResponseDto
        {
            Message = "Organization created. Check your email to confirm your account before signing in.",
            TenantId = newTenant.Id,
            RequiresEmailConfirmation = true,
            Email = newUser.Email ?? string.Empty
        });
    }

    [EnableRateLimiting("auth-submit")]
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

        if (signInResult.IsNotAllowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Confirm your email before signing in.");
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
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);

        if (user == null)
        {
            // Security: Don't reveal if user exists
            return Ok(new MessageResponse { Message = "If your email is in our system, you will receive a reset link." });
        }

        var reservation = await TryReservePasswordResetEmailSendAsync(user);
        if (!reservation.Reserved)
        {
            return Ok(new MessageResponse { Message = "If your email is in our system, you will receive a reset link." });
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
            await ReleasePasswordResetEmailReservationAsync(user);
            _logger.LogError(ex, "Failed to send password reset email for {Email}.", user.Email ?? request.Email.Trim());
        }

        return Ok(new MessageResponse { Message = "If your email is in our system, you will receive a reset link." });
    }

    [HttpGet("confirm-email")]
    public async Task<ActionResult<ConfirmEmailResultDto>> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
    {
        if (!Guid.TryParse(userId, out var parsedUserId) || string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new ConfirmEmailResultDto
            {
                Succeeded = false,
                Message = "The confirmation link is invalid."
            });
        }

        var user = await _userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == parsedUserId);
        if (user is null)
        {
            return NotFound(new ConfirmEmailResultDto
            {
                Succeeded = false,
                Message = "The confirmation link is invalid or expired."
            });
        }

        if (user.EmailConfirmed)
        {
            return Ok(new ConfirmEmailResultDto
            {
                Succeeded = true,
                AlreadyConfirmed = true,
                Email = user.Email ?? string.Empty,
                Message = "Your email is already confirmed. You can sign in."
            });
        }

        var normalizedToken = token.Trim().Replace(' ', '+');
        var result = await _userManager.ConfirmEmailAsync(user, normalizedToken);
        if (!result.Succeeded)
        {
            return BadRequest(new ConfirmEmailResultDto
            {
                Succeeded = false,
                Email = user.Email ?? string.Empty,
                Message = string.Join(" ", result.Errors.Select(e => e.Description))
            });
        }

        return Ok(new ConfirmEmailResultDto
        {
            Succeeded = true,
            AlreadyConfirmed = false,
            Email = user.Email ?? string.Empty,
            Message = "Your email has been confirmed. You can sign in."
        });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new MessageResponse { Message = "Email is required." });
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user is null)
        {
            return Ok(new MessageResponse { Message = "If your email is in our system, you will receive a confirmation link." });
        }

        if (user.EmailConfirmed)
        {
            return Ok(new MessageResponse { Message = "Your email is already confirmed. You can sign in." });
        }

        try
        {
            var sendResult = await SendEmailConfirmationAsync(user);
            if (!sendResult.Sent)
            {
                return Ok(new MessageResponse { Message = sendResult.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend confirmation email for {Email}.", user.Email);
            return StatusCode(StatusCodes.Status500InternalServerError, new MessageResponse { Message = "We could not resend the confirmation email right now. Please try again." });
        }

        return Ok(new MessageResponse { Message = "If your email is in our system, you will receive a confirmation link." });
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
            IsActive = true,
            NotifyInviteActivity = true,
            NotifySystemAlerts = true,
            EmailConfirmed = true
        };

        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
            return BadRequest("First name and last name are required.");

        var assignedRole = string.IsNullOrWhiteSpace(invite.Role) ? "Member" : invite.Role;
        await EnsureRoleAsync(assignedRole);

        // Create the user, assign the Identity role, and consume the invite atomically.
        // Identity is the only source of roles now, so a failed AddToRoleAsync must not
        // leave a role-less account behind (and the invite must remain usable).
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return BadRequest(string.Join(" ", createResult.Errors.Select(e => e.Description)));
            }

            var addRoleResult = await _userManager.AddToRoleAsync(user, assignedRole);
            if (!addRoleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return BadRequest(string.Join(" ", addRoleResult.Errors.Select(e => e.Description)));
            }

            invite.AcceptedAt = DateTime.UtcNow;
            invite.AcceptedUserId = user.Id;
            await AddInviteAcceptedNotificationsAsync(invite, user);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

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
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user == null)
        {
            return BadRequest(new MessageResponse { Message = "Invalid or expired token." });
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new MessageResponse { Message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(new MessageResponse { Message = "Password has been reset successfully." });
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
                Message = $"{displayName} joined the organization as {(string.IsNullOrWhiteSpace(invite.Role) ? "Member" : invite.Role)}.",
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

    private async Task<ConfirmationEmailSendResult> SendEmailConfirmationAsync(User user)
    {
        var reservation = await TryReserveConfirmationEmailSendAsync(user);
        if (!reservation.Reserved)
        {
            return new ConfirmationEmailSendResult(false, BuildConfirmationCooldownMessage(reservation.LastSentAt));
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var clientUrl = _config["ClientUrl"] ?? throw new InvalidOperationException("ClientUrl configuration is required.");
        var email = user.Email ?? throw new InvalidOperationException("User email is required.");
        var confirmationLink =
            $"{clientUrl.TrimEnd('/')}/confirm-email?userId={HttpUtility.UrlEncode(user.Id.ToString())}&email={HttpUtility.UrlEncode(email)}&token={HttpUtility.UrlEncode(token)}";
        var safeConfirmationLink = HttpUtility.HtmlEncode(confirmationLink);

        var body = $@"
<!doctype html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1'>
  <title>Confirm your grow.IT email</title>
</head>
<body style='margin:0;background:#f4f7fb;font-family:Arial,Helvetica,sans-serif;color:#10203c;'>
  <table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='background:#f4f7fb;padding:32px 16px;'>
    <tr>
      <td align='center'>
        <table role='presentation' width='100%' cellspacing='0' cellpadding='0' style='max-width:560px;background:#ffffff;border:1px solid #dbe3ef;'>
          <tr>
            <td style='padding:28px 28px 16px;'>
              <div style='font-size:24px;font-weight:800;color:#0f2f5f;letter-spacing:0;'>grow<span style='color:#16803d;'>.IT</span></div>
              <h1 style='margin:24px 0 8px;font-size:26px;line-height:1.25;color:#10203c;'>Confirm your email</h1>
              <p style='margin:0;color:#475569;font-size:16px;line-height:1.6;'>Welcome to grow.IT. Confirm this email address so your workspace can be activated and you can sign in.</p>
            </td>
          </tr>
          <tr>
            <td style='padding:8px 28px 24px;'>
              <a href='{safeConfirmationLink}' style='display:inline-block;background:#16803d;color:#ffffff;text-decoration:none;font-weight:700;padding:13px 20px;border-radius:6px;'>Confirm email</a>
            </td>
          </tr>
          <tr>
            <td style='padding:0 28px 24px;'>
              <p style='margin:0 0 8px;color:#64748b;font-size:13px;line-height:1.5;'>If the button does not work, copy and paste this link into your browser:</p>
              <p style='margin:0;word-break:break-all;font-size:13px;line-height:1.5;'><a href='{safeConfirmationLink}' style='color:#0f5c2d;'>{safeConfirmationLink}</a></p>
            </td>
          </tr>
          <tr>
            <td style='padding:18px 28px;background:#f8fafc;border-top:1px solid #e2e8f0;'>
              <p style='margin:0;color:#64748b;font-size:13px;line-height:1.5;'>If you did not create this account, you can ignore this email.</p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";

        try
        {
            await _emailService.SendEmailAsync(
                email,
                "Confirm your grow.IT email",
                body);
        }
        catch
        {
            await ReleaseConfirmationEmailReservationAsync(user);
            throw;
        }

        return new ConfirmationEmailSendResult(true, "Confirmation email sent.");
    }

    private async Task<ConfirmationEmailReservation> TryReserveConfirmationEmailSendAsync(User user)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(ConfirmationEmailCooldown);

        if (_context.Database.IsRelational())
        {
            var affected = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id &&
                    !u.EmailConfirmed &&
                    (u.LastConfirmationEmailSentAt == null || u.LastConfirmationEmailSentAt <= cutoff))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.LastConfirmationEmailSentAt, now)
                    .SetProperty(u => u.ConfirmationEmailSendCount, u => u.ConfirmationEmailSendCount + 1));

            if (affected == 1)
            {
                user.LastConfirmationEmailSentAt = now;
                user.ConfirmationEmailSendCount += 1;
                return new ConfirmationEmailReservation(true, now);
            }

            var lastSent = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .Select(u => u.LastConfirmationEmailSentAt)
                .FirstOrDefaultAsync();

            return new ConfirmationEmailReservation(false, lastSent);
        }

        var storedUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (storedUser is null || storedUser.EmailConfirmed)
        {
            return new ConfirmationEmailReservation(false, storedUser?.LastConfirmationEmailSentAt);
        }

        if (storedUser.LastConfirmationEmailSentAt.HasValue &&
            storedUser.LastConfirmationEmailSentAt.Value > cutoff)
        {
            return new ConfirmationEmailReservation(false, storedUser.LastConfirmationEmailSentAt);
        }

        storedUser.LastConfirmationEmailSentAt = now;
        storedUser.ConfirmationEmailSendCount += 1;
        await _context.SaveChangesAsync();

        user.LastConfirmationEmailSentAt = now;
        user.ConfirmationEmailSendCount = storedUser.ConfirmationEmailSendCount;
        return new ConfirmationEmailReservation(true, now);
    }

    private async Task ReleaseConfirmationEmailReservationAsync(User user)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.LastConfirmationEmailSentAt, (DateTime?)null)
                    .SetProperty(u => u.ConfirmationEmailSendCount, u => u.ConfirmationEmailSendCount > 0 ? u.ConfirmationEmailSendCount - 1 : 0));

            user.LastConfirmationEmailSentAt = null;
            user.ConfirmationEmailSendCount = Math.Max(0, user.ConfirmationEmailSendCount - 1);
            return;
        }

        var storedUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (storedUser is null)
        {
            return;
        }

        storedUser.LastConfirmationEmailSentAt = null;
        storedUser.ConfirmationEmailSendCount = Math.Max(0, storedUser.ConfirmationEmailSendCount - 1);
        await _context.SaveChangesAsync();

        user.LastConfirmationEmailSentAt = null;
        user.ConfirmationEmailSendCount = storedUser.ConfirmationEmailSendCount;
    }

    private static string BuildConfirmationCooldownMessage(DateTime? lastSentAt)
    {
        if (!lastSentAt.HasValue)
        {
            return "A confirmation email was already requested recently. Check your inbox before requesting another one.";
        }

        var nextAllowedAt = lastSentAt.Value.Add(ConfirmationEmailCooldown);
        var remaining = nextAllowedAt - DateTime.UtcNow;
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"A confirmation email was already sent recently. Check your inbox or try again in about {minutes} minute{(minutes == 1 ? string.Empty : "s")}.";
    }

    private sealed record ConfirmationEmailReservation(bool Reserved, DateTime? LastSentAt);
    private sealed record ConfirmationEmailSendResult(bool Sent, string Message);

    private async Task<PasswordResetEmailReservation> TryReservePasswordResetEmailSendAsync(User user)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(PasswordResetEmailCooldown);

        if (_context.Database.IsRelational())
        {
            var affected = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id &&
                    u.IsActive &&
                    (u.LastPasswordResetEmailSentAt == null || u.LastPasswordResetEmailSentAt <= cutoff))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.LastPasswordResetEmailSentAt, now)
                    .SetProperty(u => u.PasswordResetEmailSendCount, u => u.PasswordResetEmailSendCount + 1));

            if (affected == 1)
            {
                user.LastPasswordResetEmailSentAt = now;
                user.PasswordResetEmailSendCount += 1;
                return new PasswordResetEmailReservation(true);
            }

            return new PasswordResetEmailReservation(false);
        }

        var storedUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (storedUser is null || !storedUser.IsActive)
        {
            return new PasswordResetEmailReservation(false);
        }

        if (storedUser.LastPasswordResetEmailSentAt.HasValue &&
            storedUser.LastPasswordResetEmailSentAt.Value > cutoff)
        {
            return new PasswordResetEmailReservation(false);
        }

        storedUser.LastPasswordResetEmailSentAt = now;
        storedUser.PasswordResetEmailSendCount += 1;
        await _context.SaveChangesAsync();

        user.LastPasswordResetEmailSentAt = now;
        user.PasswordResetEmailSendCount = storedUser.PasswordResetEmailSendCount;
        return new PasswordResetEmailReservation(true);
    }

    private async Task ReleasePasswordResetEmailReservationAsync(User user)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.LastPasswordResetEmailSentAt, (DateTime?)null)
                    .SetProperty(u => u.PasswordResetEmailSendCount, u => u.PasswordResetEmailSendCount > 0 ? u.PasswordResetEmailSendCount - 1 : 0));

            user.LastPasswordResetEmailSentAt = null;
            user.PasswordResetEmailSendCount = Math.Max(0, user.PasswordResetEmailSendCount - 1);
            return;
        }

        var storedUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == user.Id);

        if (storedUser is null)
        {
            return;
        }

        storedUser.LastPasswordResetEmailSentAt = null;
        storedUser.PasswordResetEmailSendCount = Math.Max(0, storedUser.PasswordResetEmailSendCount - 1);
        await _context.SaveChangesAsync();

        user.LastPasswordResetEmailSentAt = null;
        user.PasswordResetEmailSendCount = storedUser.PasswordResetEmailSendCount;
    }

    private sealed record PasswordResetEmailReservation(bool Reserved);
}
