namespace GrowIT.Shared.DTOs;

/// <summary>One record returned by the top-bar search (people, programs, money sources).</summary>
public class SearchResultDto
{
    /// <summary>"Person", "HouseholdMember", "Program" or "MoneySource".</summary>
    public string Type { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;

    /// <summary>App-relative page to open for this record.</summary>
    public string Url { get; set; } = string.Empty;
}

public static class SearchResultTypes
{
    public const string Person = "Person";
    public const string HouseholdMember = "HouseholdMember";
    public const string Program = "Program";
    public const string MoneySource = "MoneySource";
}
