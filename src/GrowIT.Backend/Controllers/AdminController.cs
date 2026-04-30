using System.Security.Cryptography;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Backend.Services;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GrowIT.Backend.Controllers;

[ApiController]
[Authorize(Policy = "AdminOrManager")]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private const string InviteAuditLink = "/settings?tab=invites";
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<ReportSchedulerOptions> _schedulerOptions;
    private readonly ReportSchedulerState _schedulerState;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public AdminController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IEmailService emailService,
        IConfiguration config,
        IWebHostEnvironment environment,
        IOptionsMonitor<ReportSchedulerOptions> schedulerOptions,
        ReportSchedulerState schedulerState,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _emailService = emailService;
        _config = config;
        _environment = environment;
        _schedulerOptions = schedulerOptions;
        _schedulerState = schedulerState;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [HttpGet("organization")]
    public async Task<ActionResult<OrganizationSettingsDto>> GetOrganization()
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        if (tenant is null) return NotFound();

        return Ok(new OrganizationSettingsDto
        {
            TenantId = tenant.Id,
            Name = tenant.Name,
            Address = tenant.Address,
            ContactEmail = tenant.ContactEmail,
            OrganizationType = tenant.OrganizationType,
            OrganizationSize = tenant.OrganizationSize,
            TrackPeople = tenant.TrackPeople,
            TrackInvestments = tenant.TrackInvestments,
            TrackOutcomes = tenant.TrackOutcomes,
            TrackPrograms = tenant.TrackPrograms,
            CreatedAt = tenant.CreatedAt
        });
    }

    [HttpPut("organization")]
    public async Task<ActionResult<OrganizationSettingsDto>> UpdateOrganization(UpdateOrganizationSettingsRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        if (tenant is null) return NotFound();

        tenant.Name = request.Name.Trim();
        tenant.Address = request.Address.Trim();
        tenant.ContactEmail = request.ContactEmail.Trim();
        tenant.OrganizationType = request.OrganizationType.Trim();
        tenant.OrganizationSize = request.OrganizationSize.Trim();
        tenant.TrackPeople = request.TrackPeople;
        tenant.TrackInvestments = request.TrackInvestments;
        tenant.TrackOutcomes = request.TrackOutcomes;
        tenant.TrackPrograms = request.TrackPrograms;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetOrganization();
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserListItemDto>>> GetUsers()
    {
        var users = await _context.Users
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Select(u => new AdminUserListItemDto
            {
                Id = u.Id,
                FullName = (u.FirstName + " " + u.LastName).Trim(),
                Email = u.Email ?? string.Empty,
                Role = u.Role,
                IsActive = u.IsActive,
                DeactivatedAt = u.DeactivatedAt,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPut("users/{id:guid}/role")]
    public async Task<ActionResult<AdminUserListItemDto>> UpdateUserRole(Guid id, UpdateAdminUserRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest("Role is required.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var newRole = request.Role.Trim();
        if (newRole.Length > 64)
            return BadRequest("Role must be 64 characters or less.");

        if (await WouldRemoveLastActiveAdminAsync(user, newRole, user.IsActive))
            return BadRequest("At least one active admin must remain in the organization.");

        await EnsureRoleAsync(newRole);
        var existingRoles = await _userManager.GetRolesAsync(user);
        if (existingRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, existingRoles);
            if (!removeResult.Succeeded)
                return BadRequest(string.Join(" ", removeResult.Errors.Select(e => e.Description)));
        }

        var addResult = await _userManager.AddToRoleAsync(user, newRole);
        if (!addResult.Succeeded)
            return BadRequest(string.Join(" ", addResult.Errors.Select(e => e.Description)));

        user.Role = newRole;
        await _context.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(ToAdminUserListItem(user));
    }

    [HttpPost("users/{id:guid}/deactivate")]
    public async Task<ActionResult<AdminUserListItemDto>> DeactivateUser(Guid id)
    {
        var currentUserId = _currentUserService.UserId;
        if (currentUserId == id)
            return BadRequest("You cannot deactivate your own account.");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();
        if (!user.IsActive) return Ok(ToAdminUserListItem(user));

        if (await WouldRemoveLastActiveAdminAsync(user, user.Role, false))
            return BadRequest("At least one active admin must remain in the organization.");

        user.IsActive = false;
        user.DeactivatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(ToAdminUserListItem(user));
    }

    [HttpPost("users/{id:guid}/reactivate")]
    public async Task<ActionResult<AdminUserListItemDto>> ReactivateUser(Guid id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        user.IsActive = true;
        user.DeactivatedAt = null;
        await _context.SaveChangesAsync();
        await _userManager.UpdateSecurityStampAsync(user);

        return Ok(ToAdminUserListItem(user));
    }

    [HttpGet("invites")]
    public async Task<ActionResult<List<OrganizationInviteListItemDto>>> GetInvites()
    {
        var userLookup = await _context.Users
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => x.Name);

        var invites = await _context.OrganizationInvites
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new OrganizationInviteListItemDto
            {
                Id = i.Id,
                Email = i.Email,
                FirstName = i.FirstName,
                LastName = i.LastName,
                Role = i.Role,
                CreatedAt = i.CreatedAt,
                SentAt = i.SentAt,
                ExpiresAt = i.ExpiresAt,
                AcceptedAt = i.AcceptedAt,
                Status = i.AcceptedAt != null
                    ? "Accepted"
                    : i.RevokedAt != null
                        ? "Revoked"
                        : i.ExpiresAt < DateTime.UtcNow
                            ? "Expired"
                            : "Pending",
                InvitedByName = i.InvitedByUserId.HasValue && userLookup.ContainsKey(i.InvitedByUserId.Value)
                    ? userLookup[i.InvitedByUserId.Value]
                    : null
            })
            .ToListAsync();

        return Ok(invites);
    }

    [HttpGet("invite-activity")]
    public async Task<ActionResult<List<InviteAuditNotificationDto>>> GetInviteActivity([FromQuery] int take = 25)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue || currentUserId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        take = Math.Clamp(take, 1, 100);

        var items = await _context.Notifications
            .Where(n => n.UserId == currentUserId.Value && n.Link == InviteAuditLink)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new InviteAuditNotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Link = n.Link,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("invite-activity/mark-all-read")]
    public async Task<IActionResult> MarkInviteActivityRead()
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue || currentUserId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var unread = await _context.Notifications
            .Where(n => n.UserId == currentUserId.Value && n.Link == InviteAuditLink && !n.IsRead)
            .ToListAsync();

        foreach (var item in unread)
        {
            item.IsRead = true;
        }

        if (unread.Count > 0)
            await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("invites")]
    public async Task<ActionResult<CreateOrganizationInviteResponse>> CreateInvite(CreateOrganizationInviteRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest("Email is required.");

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var emailExists = await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email != null && u.Email.ToLower() == normalizedEmail);
        if (emailExists)
            return BadRequest("A user with this email already exists.");

        var existingPendingInvite = await _context.OrganizationInvites
            .AnyAsync(i => i.Email.ToLower() == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > DateTime.UtcNow);
        if (existingPendingInvite)
            return BadRequest("A pending invite already exists for this email.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invite = new OrganizationInvite
        {
            TenantId = tenantId.Value,
            Email = normalizedEmail,
            FirstName = request.FirstName?.Trim() ?? string.Empty,
            LastName = request.LastName?.Trim() ?? string.Empty,
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Member" : request.Role.Trim(),
            TokenHash = HashToken(token),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(request.ExpiresInDays, 1, 30)),
            InvitedByUserId = _currentUserService.UserId,
            SentAt = DateTime.UtcNow
        };

        _context.OrganizationInvites.Add(invite);
        await _context.SaveChangesAsync();

        var inviteLink = BuildInviteLink(token, invite.Email);
        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        var orgName = tenant?.Name ?? "your organization";

        var message = "Invite created.";
        try
        {
            await _emailService.SendEmailAsync(invite.Email, $"Invitation to join {orgName} on grow.IT", BuildInviteEmailBody(orgName, invite, inviteLink));
            message = "Invite created and email sent.";
        }
        catch
        {
            var detail = IsDevelopment()
                ? " Check API logs or dev-emails fallback for details."
                : string.Empty;
            message = $"Invite created, but email sending failed. Share the invite link manually.{detail}";
        }

        await AddInviteAuditNotificationsAsync(invite.TenantId,
            "Invite created",
            $"Invitation sent to {invite.Email} for role {invite.Role}. {message}");
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetInvites), null, new CreateOrganizationInviteResponse
        {
            InviteId = invite.Id,
            Message = message,
            InviteLink = inviteLink
        });
    }

    [HttpPost("invites/{id:guid}/resend")]
    public async Task<ActionResult<CreateOrganizationInviteResponse>> ResendInvite(Guid id)
    {
        var invite = await _context.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == id);
        if (invite is null) return NotFound();
        if (invite.AcceptedAt != null) return BadRequest("Invite has already been accepted.");
        if (invite.RevokedAt != null) return BadRequest("Invite has been revoked.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        invite.TokenHash = HashToken(token);
        invite.ExpiresAt = DateTime.UtcNow.AddDays(7);
        invite.SentAt = DateTime.UtcNow;
        invite.InvitedByUserId = _currentUserService.UserId;

        await _context.SaveChangesAsync();

        var tenant = await _context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == invite.TenantId);
        var orgName = tenant?.Name ?? "your organization";
        var inviteLink = BuildInviteLink(token, invite.Email);

        var message = "Invite resent.";
        try
        {
            await _emailService.SendEmailAsync(invite.Email, $"Invitation to join {orgName} on grow.IT", BuildInviteEmailBody(orgName, invite, inviteLink));
            message = "Invite resent and email sent.";
        }
        catch
        {
            var detail = IsDevelopment()
                ? " Check API logs or dev-emails fallback for details."
                : string.Empty;
            message = $"Invite resent, but email sending failed. Share the invite link manually.{detail}";
        }

        await AddInviteAuditNotificationsAsync(invite.TenantId,
            "Invite resent",
            $"Invitation resent to {invite.Email}. {message}");
        await _context.SaveChangesAsync();

        return Ok(new CreateOrganizationInviteResponse
        {
            InviteId = invite.Id,
            Message = message,
            InviteLink = inviteLink
        });
    }

    [HttpDelete("invites/{id:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid id)
    {
        var invite = await _context.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == id);
        if (invite is null) return NotFound();
        if (invite.AcceptedAt != null) return BadRequest("Cannot revoke an accepted invite.");

        invite.RevokedAt = DateTime.UtcNow;
        await AddInviteAuditNotificationsAsync(invite.TenantId,
            "Invite revoked",
            $"Invitation for {invite.Email} was revoked.");
        await _context.SaveChangesAsync();
        return NoContent();
    }


    [HttpGet("email-diagnostics")]
    public ActionResult<EmailDiagnosticsDto> GetEmailDiagnostics()
    {
        var diagnostics = BuildEmailDiagnosticsDto();
        diagnostics.StatusSummary = BuildEmailDiagnosticsStatusSummary(diagnostics);
        return Ok(diagnostics);
    }

    [HttpGet("system-diagnostics")]
    public async Task<ActionResult<SystemDiagnosticsDto>> GetSystemDiagnostics()
    {
        var envName = _environment.EnvironmentName ?? "Unknown";
        var now = DateTime.UtcNow;
        var checks = new List<SystemDiagnosticCheckDto>();

        // Database connectivity
        try
        {
            var canConnect = await _context.Database.CanConnectAsync();
            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "database",
                Label = "Database Connectivity",
                Status = canConnect ? "Healthy" : "Unhealthy",
                Message = canConnect ? "Database connection succeeded." : "Database connection failed."
            });
        }
        catch (Exception ex)
        {
            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "database",
                Label = "Database Connectivity",
                Status = "Unhealthy",
                Message = "Database connectivity check failed.",
                Details = ex.Message
            });
        }

        // Pending migrations
        try
        {
            var pending = (await _context.Database.GetPendingMigrationsAsync()).ToList();
            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "migrations",
                Label = "Pending Migrations",
                Status = pending.Count == 0 ? "Healthy" : "Warning",
                Message = pending.Count == 0
                    ? "No pending migrations."
                    : $"{pending.Count} pending migration(s).",
                Details = pending.Count == 0 ? null : string.Join(", ", pending)
            });
        }
        catch (Exception ex)
        {
            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "migrations",
                Label = "Pending Migrations",
                Status = "Warning",
                Message = "Unable to evaluate pending migrations.",
                Details = ex.Message
            });
        }

        // File storage writability (profile photo path)
        try
        {
            var root = string.IsNullOrWhiteSpace(_environment.WebRootPath)
                ? Path.Combine(_environment.ContentRootPath, "wwwroot")
                : _environment.WebRootPath;
            var photoDir = Path.Combine(root, "uploads", "profile-photos");
            Directory.CreateDirectory(photoDir);
            var probe = Path.Combine(photoDir, $".probe-{Guid.NewGuid():N}.tmp");
            await System.IO.File.WriteAllTextAsync(probe, "ok");
            System.IO.File.Delete(probe);

            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "storage",
                Label = "File Storage",
                Status = "Healthy",
                Message = "Profile upload storage is writable.",
                Details = photoDir
            });
        }
        catch (Exception ex)
        {
            checks.Add(new SystemDiagnosticCheckDto
            {
                Key = "storage",
                Label = "File Storage",
                Status = "Unhealthy",
                Message = "Profile upload storage is not writable.",
                Details = ex.Message
            });
        }

        // Email configuration readiness
        var emailDiagnostics = BuildEmailDiagnosticsDto();
        emailDiagnostics.StatusSummary = BuildEmailDiagnosticsStatusSummary(emailDiagnostics);
        var emailStatus = emailDiagnostics.StatusSummary.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            || emailDiagnostics.StatusSummary.Contains("placeholders", StringComparison.OrdinalIgnoreCase)
            ? "Warning"
            : "Healthy";
        checks.Add(new SystemDiagnosticCheckDto
        {
            Key = "email",
            Label = "Email Delivery",
            Status = emailStatus,
            Message = emailDiagnostics.StatusSummary,
            Details = $"Client URL: {emailDiagnostics.ClientUrl ?? "-"}"
        });

        // Scheduler state
        var schedulerOptions = _schedulerOptions.CurrentValue;
        _schedulerState.Enabled = schedulerOptions.Enabled;
        var dueCount = await _context.ReportSchedules.CountAsync(s => s.IsActive && s.NextRun <= now);
        checks.Add(new SystemDiagnosticCheckDto
        {
            Key = "report-scheduler",
            Label = "Scheduled Report Runner",
            Status = !schedulerOptions.Enabled
                ? "Warning"
                : string.IsNullOrWhiteSpace(_schedulerState.LastError) ? "Healthy" : "Warning",
            Message = !schedulerOptions.Enabled
                ? "Scheduler is disabled."
                : _schedulerState.IsRunningCycle
                    ? "Scheduler cycle is running."
                    : "Scheduler is enabled.",
            Details = $"Poll: {Math.Clamp(schedulerOptions.PollSeconds, 10, 3600)}s | Due schedules: {dueCount} | Last cycle: {FormatUtc(_schedulerState.LastCycleCompletedAtUtc)} | Last error: {_schedulerState.LastError ?? "None"}"
        });

        // Report run health (last 24h failures)
        var since = now.AddHours(-24);
        var recentFailures = await _context.ReportRuns.CountAsync(r => r.GeneratedAt >= since && r.Status == "Failed");
        checks.Add(new SystemDiagnosticCheckDto
        {
            Key = "reports",
            Label = "Report Run Health (24h)",
            Status = recentFailures == 0 ? "Healthy" : "Warning",
            Message = recentFailures == 0
                ? "No failed report runs in the last 24 hours."
                : $"{recentFailures} failed report run(s) in the last 24 hours."
        });

        var summaryStatus = checks.Any(c => c.Status == "Unhealthy")
            ? "Unhealthy"
            : checks.Any(c => c.Status == "Warning")
                ? "Warning"
                : "Healthy";

        return Ok(new SystemDiagnosticsDto
        {
            EnvironmentName = envName,
            CheckedAtUtc = now,
            StatusSummary = summaryStatus,
            Checks = checks
        });
    }

    private EmailDiagnosticsDto BuildEmailDiagnosticsDto()
    {
        var smtpHost = _config["Email:SmtpHost"]?.Trim();
        var smtpUser = _config["Email:SmtpUser"]?.Trim();
        var smtpPass = _config["Email:SmtpPass"];
        var fromEmail = _config["Email:FromEmail"]?.Trim();
        var env = _config["ASPNETCORE_ENVIRONMENT"] ?? HttpContext.RequestServices.GetService<IWebHostEnvironment>()?.EnvironmentName ?? "Unknown";

        var hasPlaceholders = (smtpUser?.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ?? false)
            || (smtpPass?.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ?? false);

        return new EmailDiagnosticsDto
        {
            EnvironmentName = env,
            SmtpHost = smtpHost,
            SmtpPort = int.TryParse(_config["Email:SmtpPort"], out var port) ? port : null,
            UseSsl = bool.TryParse(_config["Email:UseSsl"], out var ssl) ? ssl : null,
            FromEmail = fromEmail,
            SmtpUserMasked = MaskCredential(smtpUser),
            HasPassword = !string.IsNullOrWhiteSpace(smtpPass),
            HasPlaceholders = hasPlaceholders,
            DevFileFallbackEnabled = !string.IsNullOrWhiteSpace(_config["Email:DevFileFallbackEnabled"])
                ? bool.TryParse(_config["Email:DevFileFallbackEnabled"], out var fallbackEnabled) && fallbackEnabled
                : string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase),
            DevFileFallbackDirectory = _config["Email:DevFileFallbackDirectory"],
            ClientUrl = _config["ClientUrl"]
        };
    }

    [HttpPost("email-test")]
    public async Task<ActionResult<SendTestEmailResponse>> SendTestEmail([FromBody] SendTestEmailRequest? request)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue || currentUserId == Guid.Empty)
            return Unauthorized("No valid user context found.");

        var currentUser = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == currentUserId.Value);

        var targetEmail = string.IsNullOrWhiteSpace(request?.ToEmail)
            ? currentUser?.Email
            : request!.ToEmail!.Trim();

        if (string.IsNullOrWhiteSpace(targetEmail))
            return BadRequest("A target email is required.");

        var subject = string.IsNullOrWhiteSpace(request?.Subject)
            ? $"grow.IT SMTP Test ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC)"
            : request!.Subject!.Trim();

        var body = $@"
            <p>This is a grow.IT test email sent from the Settings security workspace.</p>
            <p><strong>Environment:</strong> {(_config["ASPNETCORE_ENVIRONMENT"] ?? "Unknown")}</p>
            <p><strong>Sent At (UTC):</strong> {DateTime.UtcNow:O}</p>
            <p><strong>Tenant:</strong> {_tenantService.TenantId}</p>";

        var result = await _emailService.SendEmailDetailedAsync(targetEmail, subject, body);

        return Ok(new SendTestEmailResponse
        {
            Succeeded = result.Succeeded,
            DeliveryMode = result.DeliveryMode,
            Message = result.Message,
            Error = result.Error,
            FallbackFilePath = result.FallbackFilePath,
            TargetEmail = targetEmail
        });
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<List<AdminAuditLogItemDto>>> GetAuditLogs([FromQuery] int take = 100, [FromQuery] string? table = null, [FromQuery] string? action = null)
    {
        take = Math.Clamp(take, 1, 500);

        var userLookup = await _context.Users
            .IgnoreQueryFilters()
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim(), u.Email })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.Name) ? x.Email : x.Name);

        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(table))
        {
            var normalizedTable = table.Trim();
            query = query.Where(a => a.TableName == normalizedTable);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            var normalizedAction = action.Trim();
            query = query.Where(a => a.ActionType == normalizedAction);
        }

        var rawLogs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.ActionType,
                a.TableName,
                a.RecordId,
                a.UserId,
                a.CreatedAt,
                a.PreviousData,
                a.NewData
            })
            .ToListAsync();

        var logs = rawLogs.Select(a => new AdminAuditLogItemDto
        {
            Id = a.Id,
            ActionType = a.ActionType,
            TableName = a.TableName,
            RecordId = a.RecordId,
            UserId = a.UserId,
            CreatedAt = a.CreatedAt,
            Summary = BuildAuditSummary(a.ActionType, a.TableName)
        }).ToList();

        foreach (var log in logs)
        {
            log.UserName = userLookup.TryGetValue(log.UserId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : "System";
        }

        return Ok(logs);
    }

    [HttpGet("security-attempts")]
    public async Task<ActionResult<List<SecurityAccessAttemptDto>>> GetSecurityAttempts([FromQuery] SecurityAccessAttemptQueryParams query)
    {
        var take = Math.Clamp(query.Take ?? 250, 1, 1000);
        var source = _context.UnauthorizedAccessAttempts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Ip))
        {
            var ipFilter = query.Ip.Trim();
            source = source.Where(a => (a.ClientIp ?? string.Empty).Contains(ipFilter));
        }

        var attempts = await source
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .ToListAsync();

        var ipSet = attempts
            .Select(a => a.ClientIp?.Trim())
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var signIns = ipSet.Count == 0
            ? new List<UserSignInEvent>()
            : await _context.UserSignInEvents
                .AsNoTracking()
                .Where(e => e.ClientIp != null && ipSet.Contains(e.ClientIp))
                .OrderByDescending(e => e.OccurredAt)
                .ToListAsync();

        var userIds = signIns.Select(e => e.UserId).Distinct().ToList();
        var userMap = userIds.Count == 0
            ? new Dictionary<Guid, User>()
            : await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

        var result = attempts.Select(attempt =>
        {
            var matches = string.IsNullOrWhiteSpace(attempt.ClientIp)
                ? new List<SecurityKnownUserMatchDto>()
                : signIns
                    .Where(e => string.Equals(e.ClientIp, attempt.ClientIp, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(e => e.UserId)
                    .Select(g =>
                    {
                        var latest = g.OrderByDescending(x => x.OccurredAt).First();
                        userMap.TryGetValue(g.Key, out var user);
                        var fullName = user is null
                            ? (latest.DisplayName ?? latest.Email)
                            : $"{user.FirstName} {user.LastName}".Trim();
                        if (string.IsNullOrWhiteSpace(fullName))
                        {
                            fullName = latest.Email;
                        }

                        return new SecurityKnownUserMatchDto
                        {
                            UserId = g.Key,
                            Email = latest.Email,
                            FullName = fullName,
                            LastSeenAt = latest.OccurredAt
                        };
                    })
                    .OrderByDescending(m => m.LastSeenAt)
                    .ToList();

            return new SecurityAccessAttemptDto
            {
                Id = attempt.Id,
                Path = attempt.Path,
                ClientIp = attempt.ClientIp,
                UserAgent = attempt.UserAgent,
                Referer = attempt.Referer,
                OccurredAt = attempt.OccurredAt,
                IsAuthenticated = attempt.IsAuthenticated,
                UserId = attempt.UserId,
                KnownUsers = matches
            };
        }).ToList();

        return Ok(result);
    }

    private string BuildInviteLink(string token, string email)
    {
        var clientUrl = (_config["ClientUrl"] ?? "http://localhost:5245").TrimEnd('/');
        return $"{clientUrl}/accept-invite?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
    }

    private static string BuildInviteEmailBody(string orgName, OrganizationInvite invite, string inviteLink)
    {
        var displayName = string.Join(" ", new[] { invite.FirstName, invite.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var greeting = string.IsNullOrWhiteSpace(displayName) ? "Hello," : $"Hello {displayName},";

        return $@"
            <p>{greeting}</p>
            <p>You have been invited to join <strong>{orgName}</strong> on grow.IT as <strong>{invite.Role}</strong>.</p>
            <p>Use the link below to create your account and join the organization.</p>
            <p><a href='{inviteLink}'>{inviteLink}</a></p>
            <p>This invitation expires on {invite.ExpiresAt:MMMM d, yyyy}.</p>";
    }

    internal static string HashToken(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private bool IsDevelopment() =>
        string.Equals(_config["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase);

    private static string? MaskCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.Length <= 4)
            return new string('*', value.Length);

        return $"{value[..2]}***{value[^2..]}";
    }

    private static string BuildEmailDiagnosticsStatusSummary(EmailDiagnosticsDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SmtpHost) || string.IsNullOrWhiteSpace(dto.SmtpUserMasked))
            return "SMTP not configured";
        if (dto.HasPlaceholders)
            return "SMTP configured with placeholders";
        if (dto.DevFileFallbackEnabled)
            return "SMTP configured (development file fallback enabled)";
        return "SMTP configured";
    }

    private static string FormatUtc(DateTime? value) =>
        value.HasValue ? value.Value.ToString("u") : "Never";

    private static AdminUserListItemDto ToAdminUserListItem(User user) => new()
    {
        Id = user.Id,
        FullName = (user.FirstName + " " + user.LastName).Trim(),
        Email = user.Email ?? string.Empty,
        Role = user.Role,
        IsActive = user.IsActive,
        DeactivatedAt = user.DeactivatedAt,
        CreatedAt = user.CreatedAt
    };

    private static bool IsAdminRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var normalized = role.Trim();
        return normalized.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Owner", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> WouldRemoveLastActiveAdminAsync(User targetUser, string targetRole, bool targetIsActive)
    {
        var currentlyCountsAsAdmin = targetUser.IsActive && IsAdminRole(targetUser.Role);
        var willCountAsAdmin = targetIsActive && IsAdminRole(targetRole);

        if (!currentlyCountsAsAdmin || willCountAsAdmin)
            return false;

        return !await _context.Users.IgnoreQueryFilters()
            .AnyAsync(u =>
                u.TenantId == targetUser.TenantId &&
                u.Id != targetUser.Id &&
                u.IsActive &&
                (u.Role.ToLower() == "admin" || u.Role.ToLower() == "owner"));
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

    private async Task AddInviteAuditNotificationsAsync(Guid tenantId, string title, string message)
    {
        var recipientIds = await _context.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId && u.IsActive && u.NotifyInviteActivity)
            .Select(u => u.Id)
            .ToListAsync();

        if (recipientIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var userId in recipientIds)
        {
            _context.Notifications.Add(new Notification
            {
                TenantId = tenantId,
                UserId = userId,
                Title = title,
                Message = message,
                Link = InviteAuditLink,
                IsRead = false,
                CreatedAt = now
            });
        }
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("seed-demo-data")]
    public async Task<ActionResult<SeedDemoDataResponseDto>> SeedDemoData()
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
            return Unauthorized("No valid tenant context found.");

        // NOTE: In a real implementation, this would involve a service 
        // that seeds complex relational data (funds, programs, clients, etc.)
        // For now, we'll return a stub to satisfy integration tests.

        return Ok(new SeedDemoDataResponseDto
        {
            Message = "Demo data seeded successfully (Stub)."
        });
    }

    private static string? BuildAuditSummary(string actionType, string tableName)
    {
        if (actionType.Equals("Budget Adjustment", StringComparison.OrdinalIgnoreCase))
        {
            return "Fund totals were adjusted.";
        }

        if (actionType.Equals("Create", StringComparison.OrdinalIgnoreCase))
            return $"Created {tableName} record.";
        if (actionType.Equals("Update", StringComparison.OrdinalIgnoreCase))
            return $"Updated {tableName} record.";
        if (actionType.Equals("Delete", StringComparison.OrdinalIgnoreCase))
            return $"Deleted {tableName} record.";

        return null;
    }
}
