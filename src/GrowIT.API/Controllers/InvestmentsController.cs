
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;

namespace GrowIT.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvestmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public InvestmentsController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvestment(CreateInvestmentRequest request)
    {
        // 1. Validate: Does the Fund exist?
        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == request.FundId);
        if (fund == null) return NotFound("Fund not found");

        // 2. Business Logic: Do we have enough money?
        if (fund.AvailableAmount < request.Amount)
        {
            return BadRequest($"Insufficient funds. Available: ${fund.AvailableAmount}");
        }

        // 3. Create the Investment
        var investment = new Investment
        {
            ClientId = request.ClientId,
            FundId = request.FundId,
            ProgramId = request.ProgramId,
            Amount = request.Amount,
            PayeeName = request.PayeeName,
            Reason = request.Reason,
            TenantId = _tenantService.TenantId ?? Guid.Empty,
            
            // Snapshot the cost (For historical data accuracy)
            SnapshotUnitCost = request.Amount, 
            
            // For now, we hardcode a 'System User' ID until we add real Login
            CreatedBy = Guid.Empty 
        };

        // 4. Update the Fund Balance (The "Real-Time" tracking)
        fund.AvailableAmount -= request.Amount;

        // 5. Save everything in one "Transaction"
        _context.Investments.Add(investment);
        _context.Funds.Update(fund); // Save the new balance
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Investment Recorded", NewFundBalance = fund.AvailableAmount });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Include the related names so the data is readable
        var investments = await _context.Investments
            .Include(i => i.Client)
            .Include(i => i.Fund) // Only works if you added 'public Fund Fund { get; set; }' to Investment entity
            .Include(i => i.Program) // Only works if you added 'public Program Program { get; set; }' to Investment entity
            .ToListAsync();
            
        return Ok(investments);
    }
}