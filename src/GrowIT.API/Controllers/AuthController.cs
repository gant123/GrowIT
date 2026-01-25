using GrowIT.API.DTOs;
using GrowIT.API.Services;
using GrowIT.Core.Entities;
using GrowIT.Core.Enums;
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
        // 1. Create the Tenant (Organization)
        var newTenant = new Tenant
        {
            Name = request.OrganizationName,
            // Defaults
            SubscriptionPlan = SubscriptionPlanType.Free 
        };

        // 2. Create the User (Admin)
        var newUser = new User
        {
            TenantId = newTenant.Id, // Link to the new Tenant
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
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
        catch (Exception)
        {
            return BadRequest("Registration failed.");
        }

        return Ok(new { Message = "Organization Registered!", TenantId = newTenant.Id });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        // 1. Find User
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == request.Email);
            
        if (user == null) return Unauthorized("Invalid credentials");

        // 2. Check Password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid credentials");
        }

        // 3. Generate Token
        var token = _tokenService.CreateToken(user, user.TenantId);

        return Ok(new { Token = token, UserId = user.Id, TenantId = user.TenantId });
    }
}