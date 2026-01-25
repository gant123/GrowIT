using System.ComponentModel.DataAnnotations;

namespace GrowIT.Client.Models;

public class CreateClientModel
{
    [Required(ErrorMessage = "Organization Name is required")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Invalid phone number")]
    public string Phone { get; set; } = string.Empty;
}