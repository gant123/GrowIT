namespace GrowIT.Client.Models;

/// <summary>
/// Represents a person in the grow.IT system - the core entity around which all support revolves.
/// This model captures identity, contact information, demographics, and stability tracking.
/// </summary>
public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Identity
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? PreferredName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    
    public string FullName => string.IsNullOrWhiteSpace(PreferredName) 
        ? $"{FirstName} {LastName}".Trim() 
        : PreferredName;
    
    public string Initials => $"{FirstName?.FirstOrDefault()}{LastName?.FirstOrDefault()}".ToUpperInvariant();
    
    // Contact
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public Address? Address { get; set; }
    public ContactPreference PreferredContactMethod { get; set; } = ContactPreference.Phone;
    
    // Demographics
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public string? ReferralSource { get; set; }
    
    // Stability Tracking (0-100 scale)
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; } = Season.Planting;
    
    // Emergency Contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    
    // System Fields
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    
    // Related Data
    public List<GrowthPlan> GrowthPlans { get; set; } = new();
    public List<Investment> Investments { get; set; } = new();
    public List<Imprint> Imprints { get; set; } = new();
    public List<PersonTag> Tags { get; set; } = new();
}

public class Address
{
    public string? Street1 { get; set; }
    public string? Street2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? County { get; set; }
    
    public string FormattedAddress => string.Join(", ", 
        new[] { Street1, Street2, City, State, ZipCode }
        .Where(s => !string.IsNullOrWhiteSpace(s)));
    
    public bool IsComplete => !string.IsNullOrWhiteSpace(Street1) 
        && !string.IsNullOrWhiteSpace(City) 
        && !string.IsNullOrWhiteSpace(State) 
        && !string.IsNullOrWhiteSpace(ZipCode);
}

public class PersonTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
}

/// <summary>
/// Season represents the current phase of a person's journey.
/// Based on the agricultural metaphor in the grow.IT vision.
/// </summary>
public enum Season
{
    /// <summary>Crisis Season - Immediate stabilization needed (red)</summary>
    Crisis = 0,
    
    /// <summary>Planting Season - Building foundation and resources (yellow/amber)</summary>
    Planting = 1,
    
    /// <summary>Growing Season - Developing independence (blue)</summary>
    Growing = 2,
    
    /// <summary>Harvest Season - Thriving and potentially giving back (green)</summary>
    Harvest = 3
}

public enum ContactPreference
{
    Phone,
    Email,
    Text,
    Mail
}

public enum EmploymentStatus
{
    Employed,
    PartTime,
    Unemployed,
    SelfEmployed,
    Retired,
    Disabled,
    Student,
    Unknown
}

public enum HousingStatus
{
    Own,
    Rent,
    Transitional,
    Homeless,
    WithFamily,
    Shelter,
    Unknown
}

// DTOs for API communication
public class PersonListDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; }
    public int TotalInvestments { get; set; }
    public decimal TotalInvested { get; set; }
    public DateTime? LastContactDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class PersonDetailDto : PersonListDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PreferredName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public Address? Address { get; set; }
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public string? ReferralSource { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public List<GrowthPlanSummaryDto> GrowthPlans { get; set; } = new();
    public List<InvestmentSummaryDto> RecentInvestments { get; set; } = new();
    public List<ImprintSummaryDto> RecentImprints { get; set; } = new();
}

public class PersonCreateDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PreferredName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Address? Address { get; set; }
    public int? HouseholdSize { get; set; }
    public int? NumberOfChildren { get; set; }
    public string? ReferralSource { get; set; }
    public string? Notes { get; set; }
}

public class PersonUpdateDto : PersonCreateDto
{
    public DateTime? DateOfBirth { get; set; }
    public decimal? AnnualIncome { get; set; }
    public EmploymentStatus? EmploymentStatus { get; set; }
    public HousingStatus? HousingStatus { get; set; }
    public int StabilityScore { get; set; }
    public Season CurrentSeason { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelationship { get; set; }
}

// Placeholder DTOs for related entities
public class GrowthPlanSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Season Season { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsActive { get; set; }
    public int GoalsCompleted { get; set; }
    public int TotalGoals { get; set; }
}

public class InvestmentSummaryDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? FundingSource { get; set; }
    public DateTime Date { get; set; }
}

public class ImprintSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}
