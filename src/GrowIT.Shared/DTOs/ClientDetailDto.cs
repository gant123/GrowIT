namespace GrowIT.Shared.DTOs;

public class ClientDetailDto
{
    // --- Profile Header ---
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string LifePhase { get; set; } = "Crisis"; // Crisis, Stable, Thriving
    public int StabilityScore { get; set; } // 1-10

    // --- Contact & Demographics ---
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int HouseholdCount { get; set; }
    public string EmploymentStatus { get; set; } = string.Empty;

    // --- Financial Summary ---
    public decimal TotalInvestment { get; set; }
    public DateTime? LastInvestmentDate { get; set; }

    // --- The Unified Timeline ---
    public List<TimelineItemDto> Timeline { get; set; } = new();
}

public class TimelineItemDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty; // "Investment", "Imprint", "Note"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? Amount { get; set; } // Only for Investments
    public string Icon { get; set; } = "oi-info";
    public string ColorClass { get; set; } = "text-secondary";
}