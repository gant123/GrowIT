using GrowIT.Core.Interfaces;
using System.ComponentModel.DataAnnotations.Schema;

namespace GrowIT.Core.Entities;

public class FamilyMember : IMustHaveTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    
    // The Link to the Head of Household
    public Guid ClientId { get; set; } 
    
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Relationship { get; set; } = "Child"; // Child, Spouse, etc.
    public DateTime? DateOfBirth { get; set; }
    public string SchoolOrEmployer { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    // Helper to calculate Age automatically
    [NotMapped]
    public int Age => DateOfBirth.HasValue 
        ? (int)((DateTime.Today - DateOfBirth.Value).TotalDays / 365.25) 
        : 0;
}
