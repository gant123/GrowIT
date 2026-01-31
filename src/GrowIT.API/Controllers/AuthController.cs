using GrowIT.Shared.DTOs;
using GrowIT.API.Services;
using GrowIT.Core.Entities;
using GrowIT.Shared.Enums;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly TokenService _tokenService;

    public AuthController(ApplicationDbContext context, TokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
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
            Role = request.Role,
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
}