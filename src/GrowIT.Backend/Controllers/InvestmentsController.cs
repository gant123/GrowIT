using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class InvestmentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public InvestmentsController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResult<InvestmentListDto>>> GetInvestments([FromQuery] InvestmentQueryParams query)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

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

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var searchTerm = query.SearchTerm.Trim();
            dbQuery = dbQuery.Where(i => 
                (i.Client != null && (i.Client.FirstName + " " + i.Client.LastName).Contains(searchTerm)) ||
                (i.FamilyMember != null && (i.FamilyMember.FirstName + " " + i.FamilyMember.LastName).Contains(searchTerm)) ||
                i.Reason.Contains(searchTerm) ||
                i.PayeeName.Contains(searchTerm));
        }

        var totalCount = await dbQuery.CountAsync();

        var items = await dbQuery
            .OrderByDescending(i => i.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new InvestmentListDto
            {
                Id = i.Id,
                Date = i.CreatedAt,
                PersonName = i.FamilyMember != null
                    ? i.FamilyMember.FirstName + " " + i.FamilyMember.LastName
                    : (i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : "Unknown"),
                Purpose = i.Reason,
                Category = i.Program != null ? i.Program.Name : string.Empty,
                FundName = i.Fund != null ? i.Fund.Name : string.Empty,
                Amount = i.Amount,
                Status = i.Status
            })
            .ToListAsync();

        return Ok(new PaginatedResult<InvestmentListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<InvestmentDetailDto>> GetInvestment(Guid id)
    {
        var detail = await FindDetailAsync(id);
        if (detail == null) return NotFound();

        return Ok(detail);
    }

    // Edit a service record. What can change depends on where it is in approval: see
    // ServiceRecordRules.MoneyLocked. Same policy as creating one, so the case manager who
    // recorded it can fix a typo; cost and money source are fixed once a manager approves it.
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateInvestment(Guid id, UpdateInvestmentRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound("Service record not found.");

        var moneyChanged = request.Amount != investment.Amount || request.FundId != investment.FundId;
        var programChanged = request.ProgramId != investment.ProgramId;

        if ((moneyChanged || programChanged) && ServiceRecordRules.MoneyLocked(investment.Status))
        {
            return BadRequest(investment.Status == InvestmentStatus.Approved
                ? "This service record is already approved, so its cost, money source and program can't change. To correct them, delete it (which releases the money) and record it again."
                : "This service record has been paid, so its cost, money source and program can't change.");
        }

        if (request.FamilyMemberId.HasValue)
        {
            var memberExists = await _context.FamilyMembers
                .AnyAsync(fm => fm.Id == request.FamilyMemberId.Value && fm.ClientId == investment.ClientId && fm.TenantId == tenantId.Value);
            if (!memberExists) return BadRequest("That household member isn't part of this person's household.");
        }

        if (programChanged)
        {
            var program = await _context.Programs
                .FirstOrDefaultAsync(p => p.Id == request.ProgramId && p.TenantId == tenantId.Value);
            if (program == null) return BadRequest("Choose a program from your organization.");

            investment.ProgramId = program.Id;
            investment.SnapshotUnitCost = program.DefaultUnitCost;
        }

        if (moneyChanged)
        {
            var fund = await _context.Funds
                .FirstOrDefaultAsync(f => f.Id == request.FundId && f.TenantId == tenantId.Value);
            if (fund == null) return BadRequest("Choose a money source from your organization.");

            // Same check as when the record was created: it must be approvable today.
            if (fund.AvailableAmount < request.Amount)
            {
                return BadRequest($"Not enough money left in {fund.Name}. Available: ${fund.AvailableAmount:N2}");
            }

            investment.FundId = fund.Id;
            investment.Amount = request.Amount;
        }

        investment.FamilyMemberId = request.FamilyMemberId;
        investment.PayeeName = request.PayeeName?.Trim() ?? string.Empty;
        investment.Reason = request.Reason?.Trim() ?? string.Empty;

        await _context.SaveChangesAsync();

        return Ok(await FindDetailAsync(id));
    }

    private async Task<InvestmentDetailDto?> FindDetailAsync(Guid id)
    {
        return await _context.Investments
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new InvestmentDetailDto
            {
                Id = i.Id,
                Date = i.CreatedAt,
                ClientId = i.ClientId,
                FamilyMemberId = i.FamilyMemberId,
                PersonName = i.FamilyMember != null
                    ? i.FamilyMember.FirstName + " " + i.FamilyMember.LastName
                    : (i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : "Unknown"),
                Purpose = i.Reason,
                ProgramId = i.ProgramId,
                Category = i.Program != null ? i.Program.Name : string.Empty,
                FundId = i.FundId,
                FundName = i.Fund != null ? i.Fund.Name : string.Empty,
                PayeeName = i.PayeeName,
                Amount = i.Amount,
                Status = i.Status
            })
            .FirstOrDefaultAsync();
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
        if (!clientExists) return BadRequest("Choose a person from your organization.");

        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == request.FundId && f.TenantId == tenantId.Value);
        if (fund == null) return NotFound("Money source not found.");

        var program = await _context.Programs.FirstOrDefaultAsync(p => p.Id == request.ProgramId && p.TenantId == tenantId.Value);
        if (program == null) return BadRequest("Choose a program from your organization.");

        if (request.FamilyMemberId.HasValue)
        {
            var memberExists = await _context.FamilyMembers
                .AnyAsync(fm => fm.Id == request.FamilyMemberId.Value && fm.ClientId == request.ClientId && fm.TenantId == tenantId.Value);
            if (!memberExists) return BadRequest("That household member isn't part of the selected person's household.");
        }

        // 2. Business Logic: Confirm the request could be approved today.
        if (fund.AvailableAmount < request.Amount)
        {
            return BadRequest($"Not enough money left in {fund.Name}. Available: ${fund.AvailableAmount:N2}");
        }

        // 3. Create the Investment
        var investment = new Investment
        {
            ClientId = request.ClientId,
            FamilyMemberId = request.FamilyMemberId,
            FundId = request.FundId,
            ProgramId = request.ProgramId,
            Amount = request.Amount,
            PayeeName = request.PayeeName?.Trim() ?? string.Empty,
            Reason = request.Reason?.Trim() ?? string.Empty,
            Status = InvestmentStatus.Pending,
            TenantId = tenantId.Value,
            SnapshotUnitCost = program.DefaultUnitCost,
            CreatedBy = _currentUserService.UserId ?? Guid.Empty 
        };

        // Pending requests do not reserve budget. Approval reserves budget; disbursement records cash leaving.
        _context.Investments.Add(investment);
        await _context.SaveChangesAsync();

        return Ok(new InvestmentActionResponse
        {
            Message = "Service record saved",
            NewFundBalance = fund.AvailableAmount,
            InvestmentId = investment.Id
        });
    }

    // Approval attribution (who/when) is recorded by the audit interceptor on the status
    // update, so no request body is needed here.
    [HttpPost("{id}/approve")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> ApproveInvestment(Guid id)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync();

        var investment = await _context.Investments
            .FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        if (investment.Status != InvestmentStatus.Pending && investment.Status != InvestmentStatus.Draft)
        {
            return BadRequest("Only pending service records can be approved.");
        }

        var fundExists = await _context.Funds.AnyAsync(f => f.Id == investment.FundId);
        if (!fundExists)
        {
            return BadRequest("The money source for this service record was not found.");
        }

        // Atomically claim the approval so two concurrent requests cannot both debit the fund.
        // On a relational store the conditional UPDATE flips Pending/Draft -> Approved in one
        // statement; only the caller that actually changes the row (claimed == 1) may debit the
        // fund. The in-memory provider (tests) keeps the simple tracked-entity path.
        if (_context.Database.IsRelational())
        {
            var claimed = await _context.Investments
                .Where(i => i.Id == id && (i.Status == InvestmentStatus.Pending || i.Status == InvestmentStatus.Draft))
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.Status, InvestmentStatus.Approved));

            if (claimed == 0)
            {
                // Another request already approved it between our read and here.
                if (transaction != null) await transaction.RollbackAsync();
                return BadRequest("Only pending service records can be approved.");
            }

            if (!await TryDecreaseFundBalanceAsync(investment.FundId, investment.Amount))
            {
                // Undo the status claim (and any partial work) by rolling the transaction back.
                if (transaction != null) await transaction.RollbackAsync();
                var available = await _context.Funds
                    .Where(f => f.Id == investment.FundId)
                    .Select(f => f.AvailableAmount)
                    .FirstOrDefaultAsync();
                return BadRequest($"Not enough money left in this money source. Available: ${available:N2}");
            }
        }
        else
        {
            if (!await TryDecreaseFundBalanceAsync(investment.FundId, investment.Amount))
            {
                var available = await _context.Funds
                    .Where(f => f.Id == investment.FundId)
                    .Select(f => f.AvailableAmount)
                    .FirstOrDefaultAsync();
                return BadRequest($"Not enough money left in this money source. Available: ${available:N2}");
            }

            investment.Status = InvestmentStatus.Approved;
            await _context.SaveChangesAsync();
        }

        if (transaction != null)
        {
            await transaction.CommitAsync();
        }

        var newBalance = await _context.Funds
            .Where(f => f.Id == investment.FundId)
            .Select(f => f.AvailableAmount)
            .FirstAsync();

        return Ok(new InvestmentActionResponse
        {
            Message = "Service record approved",
            NewFundBalance = newBalance,
            InvestmentId = investment.Id
        });
    }

    [HttpPost("{id}/disburse")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> DisburseInvestment(Guid id)
    {
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        if (investment.Status != InvestmentStatus.Approved)
        {
            return BadRequest("Only approved service records can be marked paid.");
        }

        investment.Status = InvestmentStatus.Disbursed;
        
        await _context.SaveChangesAsync();
        return Ok(new InvestmentActionResponse
        {
            Message = "Service record marked paid",
            InvestmentId = investment.Id
        });
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOrManager")]
    public async Task<IActionResult> DeleteInvestment(Guid id)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync();

        var investment = await _context.Investments
            .FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();

        if (investment.Status == InvestmentStatus.Approved)
        {
            await IncreaseFundBalanceAsync(investment.FundId, investment.Amount);
        }
        else if (investment.Status == InvestmentStatus.Disbursed || investment.Status == InvestmentStatus.Completed)
        {
            return BadRequest("Paid service records can't be deleted, because the money has already left the money source.");
        }

        _context.Investments.Remove(investment);
        await _context.SaveChangesAsync();
        if (transaction != null)
        {
            await transaction.CommitAsync();
        }
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
                return BadRequest("That household member isn't part of this person's household.");
            }
        }

        investment.FamilyMemberId = request.NewFamilyMemberId;

        await _context.SaveChangesAsync();
        return Ok();
    }

    private async Task<bool> TryDecreaseFundBalanceAsync(Guid fundId, decimal amount)
    {
        if (_context.Database.IsRelational())
        {
            var updatedFunds = await _context.Funds
                .Where(f => f.Id == fundId && f.AvailableAmount >= amount)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.AvailableAmount, f => f.AvailableAmount - amount));

            return updatedFunds > 0;
        }

        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == fundId);
        if (fund == null || fund.AvailableAmount < amount)
        {
            return false;
        }

        fund.AvailableAmount -= amount;
        return true;
    }

    private async Task IncreaseFundBalanceAsync(Guid fundId, decimal amount)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Funds
                .Where(f => f.Id == fundId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.AvailableAmount, f => f.AvailableAmount + amount));
            return;
        }

        var fund = await _context.Funds.FirstOrDefaultAsync(f => f.Id == fundId);
        if (fund != null)
        {
            fund.AvailableAmount += amount;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync()
    {
        return _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync()
            : null;
    }
}
