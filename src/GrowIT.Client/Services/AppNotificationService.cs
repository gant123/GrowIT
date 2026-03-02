namespace GrowIT.Client.Services;

public enum AppNotificationLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record AppNotificationMessage(
    string Message,
    AppNotificationLevel Level,
    int DurationMs = 4000);

public sealed class AppNotificationService
{
    private readonly Queue<AppNotificationMessage> _pending = new();
    private readonly object _sync = new();

    public event Action? Changed;

    public void Info(string message, int durationMs = 4000) =>
        Enqueue(new AppNotificationMessage(message, AppNotificationLevel.Info, durationMs));

    public void Success(string message, int durationMs = 4000) =>
        Enqueue(new AppNotificationMessage(message, AppNotificationLevel.Success, durationMs));

    public void Warning(string message, int durationMs = 5000) =>
        Enqueue(new AppNotificationMessage(message, AppNotificationLevel.Warning, durationMs));

    public void Error(string message, int durationMs = 6000) =>
        Enqueue(new AppNotificationMessage(message, AppNotificationLevel.Error, durationMs));

    public bool TryDequeue(out AppNotificationMessage? message)
    {
        lock (_sync)
        {
            if (_pending.Count == 0)
            {
                message = null;
                return false;
            }

            message = _pending.Dequeue();
            return true;
        }
    }

    private void Enqueue(AppNotificationMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Message))
        {
            return;
        }

        lock (_sync)
        {
            _pending.Enqueue(message);
        }

        Changed?.Invoke();
    }
}
