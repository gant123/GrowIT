using GrowIT.Shared.Enums;
using System.Text.Json.Serialization;
namespace GrowIT.Shared.DTOs;

public class ClientDetailDto
{
    // --- Profile Header ---
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public LifePhase LifePhase { get; set; } = LifePhase.Crisis; // Crisis, Stable, Thriving
    public int StabilityScore { get; set; } // 1-10

    // --- Contact & Demographics ---
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    [JsonIgnore]
    public string? SSNLast4 { get; set; }
    public string? MaskedSSNLast4 { get; set; }
    public string? PhotoUrl { get; set; }
    public int HouseholdCount { get; set; }
    public MaritalStatus MaritalStatus { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public DateTime? NextFollowupDate { get; set; }
    public Guid? HouseholdId { get; set; }
    // Name of the household (Households page) this person is grouped into, if any.
    public string? HouseholdName { get; set; }
    public HouseholdRole HouseholdRole { get; set; }

    // --- Financial Summary ---
    public decimal TotalInvestment { get; set; }
    public DateTime? LastInvestmentDate { get; set; }

    // --- The Unified Timeline ---
    public List<TimelineItemDto> Timeline { get; set; } = new();

    // *** NEW: The Family List ***
    public List<FamilyMemberDto> HouseholdMembers { get; set; } = new();
}


// *** NEW: Helper Class for the Kids ***
public class FamilyMemberDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public int Age { get; set; }
    public string School { get; set; } = string.Empty;
    public decimal RequestedNeed { get; set; }
    public decimal FundedAmount { get; set; }
    public decimal RemainingNeed { get; set; }
    public DateTime? LastSupportDate { get; set; }
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

/// <summary>Result of deleting a person (DELETE api/clients/{id}).</summary>
public class ClientDeleteResultDto
{
    public string Message { get; set; } = string.Empty;
    public int FollowUpsRemoved { get; set; }
    public int FamilyMembersRemoved { get; set; }
}

/// <summary>
/// 409 body when a person cannot be deleted because records still point at them. The counts say
/// what is linked so the screen can tell the user exactly what to deal with first.
/// </summary>
public class ClientDeleteBlockedDto
{
    public string Message { get; set; } = string.Empty;
    public int ServiceRecords { get; set; }
    public int Outcomes { get; set; }
    public int GrowthPlans { get; set; }
    public int Documents { get; set; }
}
