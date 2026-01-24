using GrowIT.Core.Enums;
using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class Household : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty; // "The Gant Family"
    public Guid? PrimaryClientId { get; set; }
    
    public List<Client> Members { get; set; } = new();
}

public class Client : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }
    public HouseholdRole HouseholdRole { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? SSNLast4 { get; set; }
    public string? PhotoUrl { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    
    public MaritalStatus MaritalStatus { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; }
    public int HouseholdCount { get; set; }
    public int StabilityScore { get; set; } // 1-10
    public LifePhase LifePhase { get; set; }
    
    public DateTime? NextFollowupDate { get; set; }
}

public class Document : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ClientId { get; set; }
    
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // "PDF"
    public DocumentCategory Category { get; set; }
}

public class AppTask : IMustHaveTenant // Named 'AppTask' to avoid conflict with System.Threading.Tasks.Task
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    
    public Guid AssignedTo { get; set; } // UserId
    public User? AssignedUser { get; set; }

    public DateTime DueDate { get; set; }
    public Enums.TaskStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}