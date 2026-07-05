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
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var tasks = _context.Tasks
            .Include(t => t.Client)
            .Include(t => t.AssignedUser)
            .Include(t => t.CreatedByUser)
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

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            tasks = tasks.Where(t =>
                t.Notes.Contains(search) ||
                (t.Client != null && (t.Client.FirstName.Contains(search) || t.Client.LastName.Contains(search))) ||
                (t.AssignedUser != null && (t.AssignedUser.FirstName.Contains(search) || t.AssignedUser.LastName.Contains(search))));
        }

        var result = await tasks
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskListDto
            {
                Id = t.Id,
                ClientId = t.ClientId,
                ClientName = t.Client != null ? t.Client.FirstName + " " + t.Client.LastName : "Unknown",
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedUser != null
                    ? t.AssignedUser.FirstName + " " + t.AssignedUser.LastName
                    : "Unassigned",
                CreatedByUserId = t.CreatedByUserId,
                CreatedByName = t.CreatedByUser != null
                    ? t.CreatedByUser.FirstName + " " + t.CreatedByUser.LastName
                    : null,
                Type = t.Type,
                DueDate = t.DueDate,
                Status = t.Status,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                CompletedAt = t.CompletedAt
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("assignees")]
    [Authorize(Policy = "ServiceWriter")]
    public async Task<ActionResult<List<TaskAssigneeDto>>> GetAssignees()
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId.Value && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Select(u => new TaskAssigneeDto
            {
                Id = u.Id,
                FullName = (u.FirstName + " " + u.LastName).Trim(),
                Email = u.Email ?? string.Empty,
                IsActive = u.IsActive
            })
            .ToListAsync();

        foreach (var user in users.Where(u => string.IsNullOrWhiteSpace(u.FullName)))
        {
            user.FullName = string.IsNullOrWhiteSpace(user.Email) ? "Team member" : user.Email;
        }

        return Ok(users);
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

        var now = DateTime.UtcNow;
        var task = new AppTask
        {
            TenantId = tenantId.Value,
            ClientId = request.ClientId,
            AssignedTo = request.AssignedTo,
            CreatedByUserId = _currentUserService.UserId,
            Type = request.Type,
            DueDate = ToUtcDate(request.DueDate),
            Notes = request.Notes.Trim(),
            Status = GrowIT.Shared.Enums.TaskStatus.Pending,
            CreatedAt = now
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
        task.Type = request.Type;
        task.DueDate = ToUtcDate(request.DueDate);
        task.Notes = request.Notes.Trim();
        SetTaskStatus(task, request.Status);
        task.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(await FindTaskDtoAsync(task.Id));
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

        SetTaskStatus(task, request.Status);
        task.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(await FindTaskDtoAsync(task.Id));
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

    private static void SetTaskStatus(AppTask task, GrowIT.Shared.Enums.TaskStatus status)
    {
        task.Status = status;
        task.CompletedAt = status == GrowIT.Shared.Enums.TaskStatus.Completed
            ? task.CompletedAt ?? DateTime.UtcNow
            : null;
    }

    private async Task<TaskListDto?> FindTaskDtoAsync(Guid id)
    {
        return await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new TaskListDto
            {
                Id = t.Id,
                ClientId = t.ClientId,
                ClientName = t.Client != null ? t.Client.FirstName + " " + t.Client.LastName : "Unknown",
                AssignedTo = t.AssignedTo,
                AssignedToName = t.AssignedUser != null
                    ? t.AssignedUser.FirstName + " " + t.AssignedUser.LastName
                    : "Unassigned",
                CreatedByUserId = t.CreatedByUserId,
                CreatedByName = t.CreatedByUser != null
                    ? t.CreatedByUser.FirstName + " " + t.CreatedByUser.LastName
                    : null,
                Type = t.Type,
                DueDate = t.DueDate,
                Status = t.Status,
                Notes = t.Notes,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                CompletedAt = t.CompletedAt
            })
            .FirstOrDefaultAsync();
    }
}
