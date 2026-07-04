using GrowIT.Shared.DTOs;

namespace GrowIT.Client.Services;

public interface ITaskService
{
    Task<List<TaskListDto>> GetTasksAsync(TaskQueryParams? query = null, CancellationToken ct = default);
    Task<Guid> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default);
    Task UpdateTaskAsync(Guid id, UpdateTaskRequest request, CancellationToken ct = default);
    Task UpdateTaskStatusAsync(Guid id, GrowIT.Shared.Enums.TaskStatus status, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid id, CancellationToken ct = default);
}

public class TaskService : BaseApiService, ITaskService
{
    private const string Endpoint = "api/tasks";

    public TaskService(HttpClient http, AppNotificationService notifications) : base(http, notifications) { }

    public async Task<List<TaskListDto>> GetTasksAsync(TaskQueryParams? query = null, CancellationToken ct = default)
    {
        return await GetAsync<List<TaskListDto>>($"{Endpoint}{BuildQuery(query)}", ct) ?? [];
    }

    public async Task<Guid> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct = default)
    {
        var response = await PostAsync<CreateTaskRequest, EntityCreatedResponse>(Endpoint, request, ct);
        return response?.TaskId ?? Guid.Empty;
    }

    public Task UpdateTaskAsync(Guid id, UpdateTaskRequest request, CancellationToken ct = default) =>
        PutAsync($"{Endpoint}/{id}", request, ct);

    public Task UpdateTaskStatusAsync(Guid id, GrowIT.Shared.Enums.TaskStatus status, CancellationToken ct = default) =>
        PatchAsync<UpdateTaskStatusRequest, object>($"{Endpoint}/{id}/status", new UpdateTaskStatusRequest { Status = status }, ct);

    public Task DeleteTaskAsync(Guid id, CancellationToken ct = default) =>
        DeleteAsync($"{Endpoint}/{id}", ct);

    private static string BuildQuery(TaskQueryParams? query)
    {
        if (query is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (query.ClientId.HasValue)
        {
            parts.Add($"clientId={query.ClientId}");
        }

        if (query.AssignedTo.HasValue)
        {
            parts.Add($"assignedTo={query.AssignedTo}");
        }

        if (query.Status.HasValue)
        {
            parts.Add($"status={query.Status}");
        }

        if (query.IncludeCompleted)
        {
            parts.Add("includeCompleted=true");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parts.Add($"search={Uri.EscapeDataString(query.Search)}");
        }

        parts.Add($"pageNumber={query.PageNumber}");
        parts.Add($"pageSize={query.PageSize}");

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
