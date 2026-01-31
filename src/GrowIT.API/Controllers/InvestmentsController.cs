using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.API.Controllers;

[Authorize]
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

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<InvestmentListDto>>> GetInvestments([FromQuery] InvestmentQueryParams query)
    {
        var dbQuery = _context.Investments
            .Include(i => i.Client)
            .AsQueryable();

        // Filtering
        if (query.PersonId.HasValue)
            dbQuery = dbQuery.Where(i => i.ClientId == query.PersonId.Value);
        
        if (query.Status.HasValue)
            dbQuery = dbQuery.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            dbQuery = dbQuery.Where(i => 
                (i.Client != null && (i.Client.FirstName + " " + i.Client.LastName).Contains(query.SearchTerm)) || 
                i.Reason.Contains(query.SearchTerm) || 
                i.PayeeName.Contains(query.SearchTerm));
        }

        var totalCount = await dbQuery.CountAsync();

        var items = await dbQuery
            .OrderByDescending(i => i.CreatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new InvestmentListDto
            {
                Id = i.Id,
                Date = i.CreatedAt,
                PersonName = i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : "Unknown",
                Purpose = i.Reason,
                Amount = i.Amount,
                Status = i.Status
            })
            .ToListAsync();

        return Ok(new PaginatedResult<InvestmentListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InvestmentDetailDto>> GetInvestment(Guid id)
    {
        var investment = await _context.Investments
            .Include(i => i.Client)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (investment == null) return NotFound();

        return Ok(new InvestmentDetailDto
        {
            Id = investment.Id,
            Date = investment.CreatedAt,
            PersonName = investment.Client != null ? investment.Client.FirstName + " " + investment.Client.LastName : "Unknown",
            Purpose = investment.Reason,
            Amount = investment.Amount,
            Status = investment.Status,
            Notes = investment.Reason // Mapping Reason to Notes/Description for now
        });
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
            FamilyMemberId = request.FamilyMemberId,
            FundId = request.FundId,
            ProgramId = request.ProgramId,
            Amount = request.Amount,
            PayeeName = request.PayeeName,
            Reason = request.Reason,
            Status = InvestmentStatus.Pending,
            TenantId = _tenantService.TenantId ?? Guid.Empty,
            SnapshotUnitCost = request.Amount, 
            CreatedBy = Guid.Empty 
        };

        // 4. Update the Fund Balance (The "Real-Time" tracking)
        fund.AvailableAmount -= request.Amount;

        // 5. Save everything in one "Transaction"
        _context.Investments.Add(investment);
        _context.Funds.Update(fund); 
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Investment Recorded", NewFundBalance = fund.AvailableAmount, InvestmentId = investment.Id });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> ApproveInvestment(Guid id, [FromBody] ApproveRequest request)
    {
        var investment = await _context.Investments.FindAsync(id);
        if (investment == null) return NotFound();

        investment.Status = InvestmentStatus.Approved;
        // In a real app, we'd store who approved it and when
        
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Investment Approved" });
    }

    [HttpPost("{id}/disburse")]
    public async Task<IActionResult> DisburseInvestment(Guid id)
    {
        var investment = await _context.Investments.FindAsync(id);
        if (investment == null) return NotFound();

        investment.Status = InvestmentStatus.Disbursed;
        
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Investment Disbursed" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteInvestment(Guid id)
    {
        var investment = await _context.Investments
            .Include(i => i.Fund)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        // If it's being deleted, return funds? Depends on business rules.
        // For now, let's just delete it.
        if (investment.Fund != null)
        {
            investment.Fund.AvailableAmount += investment.Amount;
        }

        _context.Investments.Remove(investment);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/reassign")]
    public async Task<IActionResult> ReassignInvestment(Guid id, [FromBody] ReassignRequest request)
    {
        var investment = await _context.Investments.FindAsync(id);
        if (investment == null) return NotFound();

        // Track the change for the AuditLog
        investment.FamilyMemberId = request.NewFamilyMemberId;
        investment.Reason = $"[REASSIGNED: {request.ReassignReason}] " + investment.Reason;

        await _context.SaveChangesAsync();
        return Ok();
    }
}

public class ReassignRequest
{
    public Guid? NewFamilyMemberId { get; set; }
    public string ReassignReason { get; set; } = string.Empty;
}

public class ApproveRequest
{
    public string ApprovedBy { get; set; } = string.Empty;
}

public class InvestmentQueryParams
{
    public Guid? PersonId { get; set; }
    public InvestmentStatus? Status { get; set; }
    public string? SearchTerm { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}