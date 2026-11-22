using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

/// <summary>
/// Record search for the top-bar "Search anything" box. Tenant-scoped (global query filter plus
/// an explicit TenantId check), small result caps, and a 2-character minimum.
/// </summary>
[Authorize]
[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    public const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;
    private const int PeopleLimit = 5;
    private const int HouseholdMemberLimit = 3;
    private const int ProgramLimit = 3;
    private const int MoneySourceLimit = 3;

    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SearchController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<ActionResult<List<SearchResultDto>>> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var term = (q ?? string.Empty).Trim();
        if (term.Length < MinQueryLength)
        {
            return Ok(new List<SearchResultDto>());
        }

        if (term.Length > MaxQueryLength)
        {
            term = term[..MaxQueryLength];
        }

        var needle = term.ToLower();
        var results = new List<SearchResultDto>();

        var people = await _context.Clients
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId.Value)
            .Where(c => (c.FirstName + " " + c.LastName).ToLower().Contains(needle))
            .OrderByDescending(c => c.FirstName.ToLower().StartsWith(needle) || c.LastName.ToLower().StartsWith(needle))
            .ThenBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Take(PeopleLimit)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.HouseholdCount })
            .ToListAsync(cancellationToken);

        results.AddRange(people.Select(c => new SearchResultDto
        {
            Type = SearchResultTypes.Person,
            Id = c.Id,
            Title = FullName(c.FirstName, c.LastName),
            Subtitle = c.HouseholdCount > 1 ? $"Person · household of {c.HouseholdCount}" : "Person",
            Url = $"/clients/{c.Id}"
        }));

        var members = await _context.FamilyMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId.Value)
            .Where(m => (m.FirstName + " " + m.LastName).ToLower().Contains(needle))
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .Take(HouseholdMemberLimit)
            .Select(m => new { m.Id, m.FirstName, m.LastName, m.Relationship })
            .ToListAsync(cancellationToken);

        results.AddRange(members.Select(m => new SearchResultDto
        {
            Type = SearchResultTypes.HouseholdMember,
            Id = m.Id,
            Title = FullName(m.FirstName, m.LastName),
            Subtitle = string.IsNullOrWhiteSpace(m.Relationship) ? "Household member" : $"Household member · {m.Relationship}",
            Url = $"/clients/member/{m.Id}"
        }));

        var programs = await _context.Programs
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId.Value)
            .Where(p => p.Name.ToLower().Contains(needle))
            .OrderBy(p => p.Name)
            .Take(ProgramLimit)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        results.AddRange(programs.Select(p => new SearchResultDto
        {
            Type = SearchResultTypes.Program,
            Id = p.Id,
            Title = p.Name,
            Subtitle = "Program",
            Url = "/programs"
        }));

        var moneySources = await _context.Funds
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId.Value)
            .Where(f => f.Name.ToLower().Contains(needle))
            .OrderBy(f => f.Name)
            .Take(MoneySourceLimit)
            .Select(f => new { f.Id, f.Name })
            .ToListAsync(cancellationToken);

        results.AddRange(moneySources.Select(f => new SearchResultDto
        {
            Type = SearchResultTypes.MoneySource,
            Id = f.Id,
            Title = f.Name,
            Subtitle = "Money source",
            Url = "/funds"
        }));

        return Ok(results);
    }

    private static string FullName(string firstName, string lastName) =>
        string.IsNullOrWhiteSpace(lastName) ? firstName.Trim() : $"{firstName.Trim()} {lastName.Trim()}";
}
