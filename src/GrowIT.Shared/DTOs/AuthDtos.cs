using System.ComponentModel.DataAnnotations;

namespace GrowIT.Shared.DTOs;

public class RegisterRequest
{
    [Required]
    public string OrganizationName { get; set; } = string.Empty; // The Tenant Name

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    // Organization Details (from Step 2)
    public string OrganizationType { get; set; } = string.Empty;
    public string OrganizationSize { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    // Initial Setup Preferences (from Step 3)
    public bool TrackPeople { get; set; } = false;
    public bool TrackInvestments { get; set; } = false;
    public bool TrackOutcomes { get; set; } = false;
    public bool TrackPrograms { get; set; } = false;
}

public class LoginRequest
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}