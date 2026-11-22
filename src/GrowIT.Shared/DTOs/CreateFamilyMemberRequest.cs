using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class CreateFamilyMemberRequest
{
    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty; // Default to Client's last name in UI
    
    [Required(ErrorMessage = "Choose a relationship.")]
    public string Relationship { get; set; } = "Child"; // Child, Spouse, etc.
    
    public DateTime? DateOfBirth { get; set; }
    
    public string SchoolOrEmployer { get; set; } = string.Empty; // e.g. "Little Cypress Elementary"
    
    public string Notes { get; set; } = string.Empty; // e.g. "Size 4 shoes, loves dinosaurs"
}