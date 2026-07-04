using GrowIT.Shared.DTOs;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Core.Entities; 

using CoreProgram = GrowIT.Core.Entities.Program;
using GrowIT.Backend.Services;
using GrowIT.Backend.Validators;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class FinancialsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public FinancialsController(ApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    // ==========================================
    // FUNDS
    // ==========================================
    [HttpPost("funds")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> CreateFund(CreateFundRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var fundName = request.Name.Trim();
        var duplicateExists = await _context.Funds.AnyAsync(f => f.Name == fundName && f.TenantId == tenantId.Value);
        if (duplicateExists)
        {
            return BadRequest("A fund with this name already exists.");
        }

        var fund = new Fund
        {
            Name = fundName,
            TotalAmount = request.TotalAmount,
            AvailableAmount = request.TotalAmount, // Starts full
            TenantId = tenantId.Value
        };

        _context.Funds.Add(fund);
        await _context.SaveChangesAsync();

        return Ok(new EntityCreatedResponse
        {
            Message = "Fund Created",
            Id = fund.Id,
            FundId = fund.Id
        });
    }

[HttpGet("funds")]
    public async Task<ActionResult<List<FundDto>>> GetFunds()
    {
        var funds = await _context.Funds
            .Select(f => new FundDto 
            {
                Id = f.Id,
                Name = f.Name,
                TotalAmount = f.TotalAmount,
                AvailableAmount = f.AvailableAmount
            })
            .ToListAsync();

        return Ok(funds);
    }
    // ==========================================
    // PROGRAMS
    // ==========================================
    [HttpPost("programs")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> CreateProgram(CreateProgramRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var programName = request.Name.Trim();
        var duplicateExists = await _context.Programs.AnyAsync(p => p.Name == programName && p.TenantId == tenantId.Value);
        if (duplicateExists)
        {
            return BadRequest("A program with this name already exists.");
        }

        var program = new CoreProgram
        {
            Name = programName,
            Description = request.Description?.Trim() ?? string.Empty,
            DefaultUnitCost = request.DefaultUnitCost,
            CapacityLimit = request.CapacityLimit,
            CapacityPeriod = request.CapacityPeriod?.Trim(),
            TenantId = tenantId.Value
        };

        _context.Programs.Add(program);
        await _context.SaveChangesAsync();

        return Ok(new EntityCreatedResponse
        {
            Message = "Program Created",
            Id = program.Id,
            ProgramId = program.Id
        });
    }

    [HttpGet("programs")]
    public async Task<ActionResult<List<ProgramDto>>> GetPrograms()
    {
        var programs = await _context.Programs
            .Select(p => new ProgramDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                DefaultUnitCost = p.DefaultUnitCost,
                CapacityLimit = p.CapacityLimit,
                CapacityPeriod = p.CapacityPeriod
            })
            .ToListAsync();

        return Ok(programs);
    }

    [HttpPut("funds/{id}")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> UpdateFund(Guid id, UpdateFundRequest request)
    {
        // 1. VALIDATION (The Enterprise Way)
        // We run the validator manually here to inject the 'id' and 'context'
        var validator = new UpdateFundRequestValidator(_context, id);
        var validationResult = await validator.ValidateAsync(request);

        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors.Select(e => e.ErrorMessage));

        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == id);
        if (fund == null) return NotFound("Fund not found.");

        var fundName = request.Name.Trim();
        var duplicateExists = await _context.Funds.AnyAsync(f => f.Id != id && f.Name == fundName && f.TenantId == fund.TenantId);
        if (duplicateExists)
        {
            return BadRequest("A fund with this name already exists.");
        }

        // 2. PREPARE DATA (Capture old state for Audit)
        var oldName = fund.Name;
        var oldTotal = fund.TotalAmount;
        var oldAvailable = fund.AvailableAmount;

        // 3. APPLY UPDATE (Logic is now safe because Validator passed)
        var realUsage = await _context.Investments
            .Where(i => i.FundId == id &&
                (i.Status == InvestmentStatus.Approved ||
                 i.Status == InvestmentStatus.Disbursed ||
                 i.Status == InvestmentStatus.Completed))
            .SumAsync(i => i.Amount);
        
        fund.Name = fundName;
        fund.TotalAmount = request.TotalAmount;
        fund.AvailableAmount = request.TotalAmount - realUsage; 

        // 4. MANUAL AUDIT (Reason)
        var audit = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = _currentUserService.UserId ?? Guid.Empty,
            TenantId = fund.TenantId,   
            ActionType = "Budget Adjustment",
            TableName = "Funds",
            RecordId = fund.Id,
            PreviousData = System.Text.Json.JsonSerializer.Serialize(new { Total = oldTotal, Available = oldAvailable, Name = oldName }),
            NewData = System.Text.Json.JsonSerializer.Serialize(new { Total = request.TotalAmount, Available = fund.AvailableAmount, Reason = request.ChangeReason }),
            CreatedAt = DateTime.UtcNow
        };

        _context.AuditLogs.Add(audit);
        await _context.SaveChangesAsync();

        return Ok(fund);
    }
}
