using System.Security.Cryptography;
using GrowIT.Core.Entities;
using GrowIT.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GrowIT.Core.Interfaces;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ImprintsController : ControllerBase
{
    private const int FollowUpNotesMaxLength = 500;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public ImprintsController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> CreateImprint(CreateImprintRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        // 1. Validation & Integrity Checks

        // Verify Client exists and belongs to tenant
        var client = await _context.Clients
            .AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId);
        if (!client)
            return BadRequest("Choose a person from your organization.");

        var referenceError = await ValidateReferencesAsync(request.ClientId, request.FamilyMemberId, request.InvestmentId, tenantId.Value);
        if (referenceError is not null)
            return referenceError;

        // 2. Create the Milestone (Imprint)
        var imprint = new Imprint
        {
            ClientId = request.ClientId,       // Required: Links to the Family
            FamilyMemberId = request.FamilyMemberId, // Optional: Links to specific person
            InvestmentId = request.InvestmentId, // Optional

            Title = request.Title.Trim(),
            Category = request.Category,
            Outcome = request.Outcome,
            Notes = request.Notes?.Trim() ?? string.Empty,

            // Ensure Dates are UTC for Postgres
            DateOccurred = DateTime.SpecifyKind(request.DateOccurred, DateTimeKind.Utc),
            FollowupDate = request.FollowupDate.HasValue
                ? DateTime.SpecifyKind(request.FollowupDate.Value, DateTimeKind.Utc)
                : null,

            TenantId = tenantId.Value
        };

        // 3. Save (with its follow-up, when a follow-up date was given)
        _context.Imprints.Add(imprint);
        await SyncFollowUpAsync(imprint, previousFollowupDate: null, isNew: true, tenantId.Value);
        await _context.SaveChangesAsync();

        var response = await FindResponseAsync(imprint.Id);
        return CreatedAtAction(nameof(GetById), new { id = imprint.Id }, response);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ImprintResponseDto>> GetById(Guid id)
    {
        var imprint = await FindResponseAsync(id);
        if (imprint == null) return NotFound();

        return Ok(imprint);
    }

    // Edit a saved outcome. Same policy as adding one. The person stays fixed; the linked
    // follow-up moves with the follow-up date (or is removed when the date is cleared).
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<ImprintResponseDto>> UpdateImprint(Guid id, UpdateImprintRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var imprint = await _context.Imprints.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId.Value);
        if (imprint == null) return NotFound("Outcome not found.");

        var referenceError = await ValidateReferencesAsync(imprint.ClientId, request.FamilyMemberId, request.InvestmentId, tenantId.Value);
        if (referenceError is not null)
            return referenceError;

        var previousFollowupDate = imprint.FollowupDate;

        imprint.FamilyMemberId = request.FamilyMemberId;
        imprint.InvestmentId = request.InvestmentId;
        imprint.Title = request.Title.Trim();
        imprint.Category = request.Category;
        imprint.Outcome = request.Outcome;
        imprint.Notes = request.Notes?.Trim() ?? string.Empty;
        if (request.DateOccurred != default)
        {
            imprint.DateOccurred = DateTime.SpecifyKind(request.DateOccurred, DateTimeKind.Utc);
        }
        imprint.FollowupDate = request.FollowupDate.HasValue
            ? DateTime.SpecifyKind(request.FollowupDate.Value, DateTimeKind.Utc)
            : null;

        await SyncFollowUpAsync(imprint, previousFollowupDate, isNew: false, tenantId.Value);
        await _context.SaveChangesAsync();

        return Ok(await FindResponseAsync(id));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteImprint(Guid id)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var imprint = await _context.Imprints.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId.Value);
        if (imprint == null) return NotFound("Outcome not found.");

        // An open follow-up for a deleted outcome would point at nothing; finished ones stay as history.
        var followUpId = FollowUpTaskIdFor(imprint.Id);
        var followUp = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == followUpId);
        if (followUp is { Status: GrowIT.Shared.Enums.TaskStatus.Pending })
        {
            _context.Tasks.Remove(followUp);
        }

        _context.Imprints.Remove(imprint);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("member/{memberId}")]
    public async Task<ActionResult<List<ImprintResponseDto>>> GetImprintsForMember(Guid memberId)
    {
        var tenantId = _tenantService.TenantId;

        // Fetch all milestones for a specific family member, enforced by tenant
        var imprints = await _context.Imprints
            .AsNoTracking()
            .Where(i => i.FamilyMemberId == memberId && i.TenantId == tenantId)
            .OrderByDescending(i => i.DateOccurred)
            .Select(i => new ImprintResponseDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                FamilyMemberId = i.FamilyMemberId,
                InvestmentId = i.InvestmentId,
                Title = i.Title,
                DateOccurred = i.DateOccurred,
                Category = i.Category,
                Outcome = i.Outcome,
                Notes = i.Notes,
                FollowupDate = i.FollowupDate
            })
            .ToListAsync();

        return Ok(imprints);
    }

    [HttpGet]
    public async Task<ActionResult<List<ImprintListDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var tenantId = _tenantService.TenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var imprints = await _context.Imprints
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.DateOccurred)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ImprintListDto
            {
                Id = i.Id,
                PersonId = i.FamilyMemberId ?? i.ClientId,
                PersonName = i.FamilyMember != null
                    ? (i.FamilyMember.FirstName + " " + i.FamilyMember.LastName)
                    : (i.Client != null ? (i.Client.FirstName + " " + i.Client.LastName) : "Unknown"),
                Title = i.Title,
                Category = i.Category,
                Outcome = i.Outcome,
                Date = i.DateOccurred,
                FollowupDate = i.FollowupDate,
                // Nothing marks an outcome as verified yet (no stored verification), so never claim it.
                IsVerified = false
            })
            .ToListAsync();

        return Ok(imprints);
    }

    /// <summary>
    /// Each outcome has at most one follow-up, and its id is derived from the outcome id. Saving
    /// the same outcome again (an edit, a retry, a double click) therefore finds and updates that
    /// follow-up instead of adding another one, without needing an extra column to link them.
    /// </summary>
    internal static Guid FollowUpTaskIdFor(Guid outcomeId)
    {
        Span<byte> input = stackalloc byte[32];
        FollowUpNamespace.TryWriteBytes(input[..16]);
        outcomeId.TryWriteBytes(input[16..]);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);

        // Shape it as a name-based (version 5, RFC 4122) UUID.
        hash[7] = (byte)((hash[7] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16]);
    }

    private static readonly Guid FollowUpNamespace = new("4f1d7c38-0d0e-4a43-9f5e-0b6a7d1c2e51");

    /// <summary>
    /// Keeps the outcome's follow-up in Follow-ups in step with its follow-up date. Changes are
    /// staged on the context and saved together with the outcome.
    /// </summary>
    private async Task SyncFollowUpAsync(Imprint imprint, DateTime? previousFollowupDate, bool isNew, Guid tenantId)
    {
        var taskId = FollowUpTaskIdFor(imprint.Id);
        var task = isNew ? null : await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        var now = DateTime.UtcNow;

        if (!imprint.FollowupDate.HasValue)
        {
            // Date cleared: drop the open follow-up. A completed or skipped one stays as history.
            if (task is { Status: GrowIT.Shared.Enums.TaskStatus.Pending })
            {
                _context.Tasks.Remove(task);
            }
            return;
        }

        var dueDate = DateTime.SpecifyKind(imprint.FollowupDate.Value.Date, DateTimeKind.Utc);
        var dateChanged = previousFollowupDate?.Date != dueDate.Date;
        var notes = BuildFollowUpNotes(imprint.Title);

        if (task is null)
        {
            // If someone deleted the follow-up in Follow-ups, an unrelated edit (title, notes)
            // should not bring it back. A new or changed date does.
            if (!isNew && !dateChanged) return;

            var assigneeId = await ResolveFollowUpAssigneeAsync(tenantId);
            if (assigneeId is null) return;

            _context.Tasks.Add(new AppTask
            {
                Id = taskId,
                TenantId = tenantId,
                ClientId = imprint.ClientId,
                AssignedTo = assigneeId.Value,
                CreatedByUserId = assigneeId.Value,
                Type = ActionItemType.ClientFollowUp,
                DueDate = dueDate,
                Notes = notes,
                Status = GrowIT.Shared.Enums.TaskStatus.Pending,
                CreatedAt = now
            });
            return;
        }

        if (task.Status == GrowIT.Shared.Enums.TaskStatus.Pending)
        {
            task.ClientId = imprint.ClientId;
            task.DueDate = dueDate;
            task.Notes = notes;
            task.UpdatedAt = now;
        }
        else if (dateChanged)
        {
            // The last follow-up was done; a new date means another one is needed.
            task.ClientId = imprint.ClientId;
            task.DueDate = dueDate;
            task.Notes = notes;
            task.Status = GrowIT.Shared.Enums.TaskStatus.Pending;
            task.CompletedAt = null;
            task.UpdatedAt = now;
        }
    }

    // The follow-up goes to whoever recorded the outcome. Follow-ups must be assigned to an
    // active team member of this organization; if the current user isn't one, none is added.
    private async Task<Guid?> ResolveFollowUpAssigneeAsync(Guid tenantId)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return null;

        var isActiveMember = await _context.Users
            .AnyAsync(u => u.Id == userId.Value && u.TenantId == tenantId && u.IsActive);
        return isActiveMember ? userId : null;
    }

    private static string BuildFollowUpNotes(string outcomeTitle)
    {
        var notes = $"Follow up: {outcomeTitle.Trim()}";
        return notes.Length <= FollowUpNotesMaxLength ? notes : notes[..FollowUpNotesMaxLength];
    }

    private async Task<ActionResult?> ValidateReferencesAsync(Guid clientId, Guid? familyMemberId, Guid? investmentId, Guid tenantId)
    {
        // The household member must belong to that person (and by extension this organization).
        if (familyMemberId.HasValue)
        {
            var familyMemberExists = await _context.FamilyMembers
                .AnyAsync(f => f.Id == familyMemberId && f.ClientId == clientId && f.TenantId == tenantId);

            if (!familyMemberExists)
                return BadRequest("That household member isn't part of this person's household.");
        }

        // A linked service record must exist and belong to the same person.
        if (investmentId.HasValue)
        {
            var investmentExists = await _context.Investments
                .AnyAsync(i => i.Id == investmentId && i.ClientId == clientId && i.TenantId == tenantId);

            if (!investmentExists)
                return NotFound("That service record wasn't found for this person.");
        }

        return null;
    }

    private async Task<ImprintResponseDto?> FindResponseAsync(Guid id)
    {
        var tenantId = _tenantService.TenantId;
        return await _context.Imprints
            .AsNoTracking()
            .Where(i => i.Id == id && i.TenantId == tenantId)
            .Select(i => new ImprintResponseDto
            {
                Id = i.Id,
                ClientId = i.ClientId,
                FamilyMemberId = i.FamilyMemberId,
                InvestmentId = i.InvestmentId,
                Title = i.Title,
                DateOccurred = i.DateOccurred,
                Category = i.Category,
                Outcome = i.Outcome,
                Notes = i.Notes,
                FollowupDate = i.FollowupDate,
                ClientName = i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : string.Empty,
                PersonName = i.FamilyMember != null
                    ? i.FamilyMember.FirstName + " " + i.FamilyMember.LastName
                    : (i.Client != null ? i.Client.FirstName + " " + i.Client.LastName : "Unknown"),
                InvestmentSummary = i.Investment != null
                    ? i.Investment.Reason
                    : null
            })
            .FirstOrDefaultAsync();
    }
}
