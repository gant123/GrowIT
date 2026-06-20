using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DocumentsController(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetDocuments([FromQuery] Guid? clientId = null)
    {
        var query = _context.Documents
            .Join(_context.Clients,
                document => document.ClientId,
                client => client.Id,
                (document, client) => new { document, client });

        if (clientId.HasValue)
        {
            query = query.Where(x => x.document.ClientId == clientId.Value);
        }

        var documents = await query
            .OrderBy(x => x.client.LastName)
            .ThenBy(x => x.client.FirstName)
            .Select(x => new DocumentDto
            {
                Id = x.document.Id,
                ClientId = x.document.ClientId,
                ClientName = x.client.FirstName + " " + x.client.LastName,
                FileUrl = x.document.FileUrl,
                FileType = x.document.FileType,
                Category = x.document.Category
            })
            .ToListAsync();

        return Ok(documents);
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> CreateDocument(CreateDocumentRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId.Value);
        if (!clientExists)
        {
            return BadRequest("Selected client was not found.");
        }

        var document = new Document
        {
            TenantId = tenantId.Value,
            ClientId = request.ClientId,
            FileUrl = request.FileUrl,
            FileType = request.FileType,
            Category = request.Category
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Document created", DocumentId = document.Id });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateDocument(Guid id, UpdateDocumentRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (document is null)
        {
            return NotFound("Document not found.");
        }

        var clientExists = await _context.Clients.AnyAsync(c => c.Id == request.ClientId && c.TenantId == tenantId.Value);
        if (!clientExists)
        {
            return BadRequest("Selected client was not found.");
        }

        document.ClientId = request.ClientId;
        document.FileUrl = request.FileUrl;
        document.FileType = request.FileType;
        document.Category = request.Category;

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == id);
        if (document is null)
        {
            return NotFound("Document not found.");
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
