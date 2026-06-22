namespace GrowIT.Client.Services;

/// <summary>
/// Lets a detail page supply a friendly label for the current route's last breadcrumb
/// segment (e.g. a client's name instead of the generic "Case File"). The shell
/// (<c>MainLayout</c>) reads this when building breadcrumbs and re-renders on change.
/// Scoped per circuit, so it never leaks a label across users.
/// </summary>
public sealed class BreadcrumbState
{
    /// <summary>Absolute path the <see cref="Label"/> applies to (e.g. "/clients/{id}").</summary>
    public string? Path { get; private set; }

    /// <summary>Friendly label to show for the last crumb on <see cref="Path"/>.</summary>
    public string? Label { get; private set; }

    public event Action? Changed;

    /// <summary>Records a friendly label for a specific route path.</summary>
    public void Set(string path, string label)
    {
        if (string.Equals(Path, path, StringComparison.OrdinalIgnoreCase) && Label == label)
        {
            return;
        }

        Path = path;
        Label = label;
        Changed?.Invoke();
    }

    /// <summary>Returns the friendly label for <paramref name="path"/>, or null if none applies.</summary>
    public string? LabelFor(string path) =>
        !string.IsNullOrWhiteSpace(Label) && string.Equals(Path, path, StringComparison.OrdinalIgnoreCase)
            ? Label
            : null;
}
