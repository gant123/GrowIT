namespace GrowIT.Client;

/// <summary>
/// Cache-busting token appended to static assets (css/js) as <c>?v=…</c>. Derived from the
/// running build so it advances automatically on every deploy — no manual version bump
/// required. Uses the client assembly's file timestamp (stable within an image, changes on
/// each rebuild), falling back to a per-process value if the location is unavailable.
/// </summary>
public static class BuildInfo
{
    public static string AssetVersion { get; } = Compute();

    private static string Compute()
    {
        try
        {
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location) && System.IO.File.Exists(location))
            {
                return System.IO.File.GetLastWriteTimeUtc(location).ToString("yyyyMMddHHmmss");
            }
        }
        catch
        {
            // Fall through to a per-process token (e.g. single-file publish with no location).
        }

        return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    }
}
