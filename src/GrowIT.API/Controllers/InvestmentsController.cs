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
            .Include(i => i.FamilyMember)
            .Include(i => i.Program)
            .AsQueryable();

        // Filtering
        if (query.PersonId.HasValue)
            dbQuery = dbQuery.Where(i => i.ClientId == query.PersonId.Value || i.FamilyMemberId == query.PersonId.Value);
        
        if (query.Status.HasValue)
            dbQuery = dbQuery.Where(i => i.Status == query.Status.Value);

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            dbQuery = dbQuery.Where(i => 
                (i.Client != null && (i.Client.FirstName + " " + i.Client.LastName).Contains(query.SearchTerm)) ||
                (i.FamilyMember != null && (i.FamilyMember.FirstName + " " + i.FamilyMember.LastName).Contains(query.SearchTerm)) ||
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
                PersonName = i.FamilyMember != null
                    ? i.FamilyMember.FirstName + " " + i.FamilyMember.LastName
                    : (i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : "Unknown"),
                Purpose = i.Reason,
                Category = i.Program != null ? i.Program.Name : string.Empty,
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
            .Include(i => i.FamilyMember)
            .Include(i => i.Program)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (investment == null) return NotFound();

        return Ok(new InvestmentDetailDto
        {
            Id = investment.Id,
            Date = investment.CreatedAt,
            PersonName = investment.FamilyMember != null
                ? investment.FamilyMember.FirstName + " " + investment.FamilyMember.LastName
                : (investment.Client != null ? investment.Client.FirstName + " " + investment.Client.LastName : "Unknown"),
            Purpose = investment.Reason,
            Category = investment.Program?.Name ?? string.Empty,
            Amount = investment.Amount,
            Status = investment.Status,
            Notes = investment.Reason // Mapping Reason to Notes/Description for now
        });
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> CreateInvestment(CreateInvestmentRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        // 1. Validate core references inside this tenant
        var clientExists = await _context.Clients.AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId.Value);
        if (!clientExists) return BadRequest("Invalid client.");

        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == request.FundId && f.TenantId == tenantId.Value);
        if (fund == null) return NotFound("Fund not found");

        var programExists = await _context.Programs.AnyAsync(p => p.Id == request.ProgramId && p.TenantId == tenantId.Value);
        if (!programExists) return BadRequest("Invalid program.");

        if (request.FamilyMemberId.HasValue)
        {
            var memberExists = await _context.FamilyMembers
                .AnyAsync(fm => fm.Id == request.FamilyMemberId.Value && fm.ClientId == request.ClientId);
            if (!memberExists) return BadRequest("Family member not found for the selected client.");
        }

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
            TenantId = tenantId.Value,
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
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> ApproveInvestment(Guid id, [FromBody] ApproveInvestmentRequest request)
    {
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        investment.Status = InvestmentStatus.Approved;
        // In a real app, we'd store who approved it and when
        
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Investment Approved" });
    }

    [HttpPost("{id}/disburse")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> DisburseInvestment(Guid id)
    {
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        investment.Status = InvestmentStatus.Disbursed;
        
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Investment Disbursed" });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOrManager")]
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
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> ReassignInvestment(Guid id, [FromBody] ReassignInvestmentRequest request)
    {
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        if (request.NewFamilyMemberId.HasValue)
        {
            var validTarget = await _context.FamilyMembers
                .AnyAsync(fm => fm.Id == request.NewFamilyMemberId.Value && fm.ClientId == investment.ClientId);

            if (!validTarget)
            {
                return BadRequest("Target family member must belong to the same client.");
            }
        }

        // Track the change for the AuditLog
        investment.FamilyMemberId = request.NewFamilyMemberId;
        investment.Reason = $"[REASSIGNED: {request.ReassignReason}] " + investment.Reason;

        await _context.SaveChangesAsync();
        return Ok();
    }
}
