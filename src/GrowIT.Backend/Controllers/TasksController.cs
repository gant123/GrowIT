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
public class TasksController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public TasksController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpGet]
    public async Task<ActionResult<List<TaskListDto>>> GetTasks([FromQuery] TaskQueryParams query)
    {
        var tasks = _context.Tasks
            .Include(t => t.Client)
            .Include(t => t.AssignedUser)
            .AsQueryable();

        if (query.ClientId.HasValue)
        {
            tasks = tasks.Where(t => t.ClientId == query.ClientId.Value);
        }

        if (query.AssignedTo.HasValue)
        {
            tasks = tasks.Where(t => t.AssignedTo == query.AssignedTo.Value);
        }

        if (query.Status.HasValue)
        {
            tasks = tasks.Where(t => t.Status == query.Status.Value);
        }
        else if (!query.IncludeCompleted)
        {
            tasks = tasks.Where(t => t.Status == GrowIT.Shared.Enums.TaskStatus.Pending);
        }

        var result = await tasks
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate)
            .Select(t => new TaskListDto
            {
                Id = t.Id,
                ClientId = t.ClientId,
                ClientName = t.Client != null ? t.Client.FirstName + " " + t.Client.LastName : "Unknown",
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedUser != null
                    ? t.AssignedUser.FirstName + " " + t.AssignedUser.LastName
                    : "Unassigned",
                DueDate = t.DueDate,
                Status = t.Status,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> CreateTask(CreateTaskRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var validationError = await ValidateTaskReferencesAsync(request.ClientId, request.AssignedTo, tenantId.Value);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var task = new AppTask
        {
            TenantId = tenantId.Value,
            ClientId = request.ClientId,
            AssignedTo = request.AssignedTo,
            DueDate = ToUtcDate(request.DueDate),
            Notes = request.Notes,
            Status = GrowIT.Shared.Enums.TaskStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        return Ok(new EntityCreatedResponse
        {
            Message = "Task created",
            Id = task.Id,
            TaskId = task.Id
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateTask(Guid id, UpdateTaskRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return NotFound("Task not found.");
        }

        var validationError = await ValidateTaskReferencesAsync(request.ClientId, request.AssignedTo, tenantId.Value);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        task.ClientId = request.ClientId;
        task.AssignedTo = request.AssignedTo;
        task.DueDate = ToUtcDate(request.DueDate);
        task.Notes = request.Notes;
        task.Status = request.Status;

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> UpdateTaskStatus(Guid id, UpdateTaskStatusRequest request)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return NotFound("Task not found.");
        }

        task.Status = request.Status;
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<IActionResult> DeleteTask(Guid id)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            return NotFound("Task not found.");
        }

        _context.Tasks.Remove(task);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<string?> ValidateTaskReferencesAsync(Guid clientId, Guid assignedTo, Guid tenantId)
    {
        var clientExists = await _context.Clients.AnyAsync(c => c.Id == clientId && c.TenantId == tenantId);
        if (!clientExists)
        {
            return "Selected client was not found.";
        }

        var userExists = await _context.Users.AnyAsync(u => u.Id == assignedTo && u.TenantId == tenantId && u.IsActive);
        if (!userExists)
        {
            return "Selected assignee was not found or is inactive.";
        }

        return null;
    }

    private static DateTime ToUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
}
