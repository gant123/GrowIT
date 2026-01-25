using GrowIT.Shared.Enums;
using GrowIT.Core.Interfaces;

namespace GrowIT.Core.Entities;

public class Program : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DefaultUnitCost { get; set; }
}

public class Fund : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AvailableAmount { get; set; }
}

public class Investment : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    public Guid ClientId { get; set; }
    public Client? Client { get; set; }
    
    public Guid FundId { get; set; }
    public Fund? Fund { get; set; } 

    public Guid ProgramId { get; set; }
    public Program? Program { get; set; } 
    
    public decimal Amount { get; set; }
    public decimal SnapshotUnitCost { get; set; }
    
    public string PayeeName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    
    public Guid CreatedBy { get; set; } 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? FamilyMemberId { get; set; } // Nullable because some things (like Rent) are for the whole family
    public FamilyMember? FamilyMember { get; set; }
}

public class Imprint : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid InvestmentId { get; set; }
    public Investment? Investment { get; set; }
    public ImpactOutcome Outcome { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime? FollowupDate { get; set; }
}