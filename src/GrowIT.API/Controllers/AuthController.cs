using GrowIT.Shared.DTOs;
using GrowIT.API.Services;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.Enums;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Web;

namespace GrowIT.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public AuthController(
        ApplicationDbContext context, 
        TokenService tokenService, 
        IEmailService emailService,
        IConfiguration config)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        // 0. Check if user already exists
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == request.Email);
            
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
            Email = request.Email,
            Role = string.IsNullOrEmpty(request.Role) ? "Admin" : request.Role, // Ensure a role is set
            // Hash the password immediately!
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        // 3. Save as a Transaction (All or Nothing)
        using var transaction = _context.Database.BeginTransaction();
        try 
        {
            _context.Tenants.Add(newTenant);
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = "Registration failed.", Error = ex.Message, InnerError = ex.InnerException?.Message });
        }

        return Ok(new { Message = "Organization Registered!", TenantId = newTenant.Id });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        // 1. Find User
        // Use IgnoreQueryFilters() to find user across all tenants during login
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);
            
        if (user == null) 
        {
            // Security Best Practice: Don't reveal if user exists
            return Unauthorized("Invalid email or password");
        }

        // 2. Check Password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid email or password");
        }

        // 3. Generate Token
        // Important: Use user.TenantId to ensure the token has the correct tenant context
        var token = _tokenService.CreateToken(user, user.TenantId);

        return Ok(new { Token = token, UserId = user.Id, TenantId = user.TenantId });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null)
        {
            // Security: Don't reveal if user exists
            return Ok(new { Message = "If your email is in our system, you will receive a reset link." });
        }

        // Generate token
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        user.PasswordResetToken = token;
        user.ResetTokenExpires = DateTime.UtcNow.AddHours(2);

        await _context.SaveChangesAsync();

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
            await _emailService.SendEmailAsync(user.Email, "Reset your GrowIT password", body);
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

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.PasswordResetToken == request.Token);

        if (user == null || user.ResetTokenExpires < DateTime.UtcNow)
        {
            return BadRequest(new { Message = "Invalid or expired token." });
        }

        // Reset password
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.ResetTokenExpires = null;

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Password has been reset successfully." });
    }
}