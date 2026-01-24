using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class AuditLog : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    
    public string ActionType { get; set; } = string.Empty; // CREATE, UPDATE
    public string TableName { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    
    public string? PreviousData { get; set; } // JSON
    public string? NewData { get; set; }      // JSON
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public bool IsRead { get; set; } = false;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class BillingEvent : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    
    public string EventType { get; set; } = string.Empty; // "subscription.updated"
    public string ReferenceTable { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    
    public string Metadata { get; set; } = "{}"; // JSON
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}