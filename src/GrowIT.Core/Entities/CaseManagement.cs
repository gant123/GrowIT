using GrowIT.Shared.Enums;
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

    // Null = an organization-level document (IRS letter, bylaws, budget); otherwise the person
    // (head of household) the document belongs to.
    public Guid? ClientId { get; set; }

    // Legacy link-style rows only: a web address typed by the user. Empty for uploaded files.
    public string FileUrl { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty; // Short label, e.g. "PDF"
    public DocumentCategory Category { get; set; }

    // Optional friendly name ("IRS determination letter"); the UI falls back to the file name.
    public string? Title { get; set; }

    // Uploaded-file metadata. StoragePath is a server-generated key relative to the private
    // document store (never the user's file name, never a public URL). Null for legacy links.
    public string? StoragePath { get; set; }
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
}

public class AppTask : IMustHaveTenant // Named 'AppTask' to avoid conflict with System.Threading.Tasks.Task
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    
    public Guid AssignedTo { get; set; } // UserId
    public User? AssignedUser { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public GrowIT.Shared.Enums.ActionItemType Type { get; set; } = GrowIT.Shared.Enums.ActionItemType.ClientFollowUp;
    public DateTime DueDate { get; set; }
    public GrowIT.Shared.Enums.TaskStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
