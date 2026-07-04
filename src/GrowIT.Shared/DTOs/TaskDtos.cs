using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class TaskListDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public Guid AssignedTo { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public GrowIT.Shared.Enums.TaskStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsOverdue => Status == GrowIT.Shared.Enums.TaskStatus.Pending && DueDate.Date < DateTime.UtcNow.Date;
}

public class TaskQueryParams
{
    public Guid? ClientId { get; set; }
    public Guid? AssignedTo { get; set; }
    public GrowIT.Shared.Enums.TaskStatus? Status { get; set; }
    public bool IncludeCompleted { get; set; }

    [MaxLength(200)]
    public string? Search { get; set; }

    [Range(1, 100000)]
    public int PageNumber { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 100;
}

public class CreateTaskRequest
{
    [Required]
    public Guid ClientId { get; set; }

    [Required]
    public Guid AssignedTo { get; set; }

    [Required]
    public DateTime DueDate { get; set; } = DateTime.UtcNow.Date.AddDays(7);

    [Required]
    public string Notes { get; set; } = string.Empty;
}

public class UpdateTaskRequest : CreateTaskRequest
{
    public GrowIT.Shared.Enums.TaskStatus Status { get; set; } = GrowIT.Shared.Enums.TaskStatus.Pending;
}

public class UpdateTaskStatusRequest
{
    public GrowIT.Shared.Enums.TaskStatus Status { get; set; }
}
