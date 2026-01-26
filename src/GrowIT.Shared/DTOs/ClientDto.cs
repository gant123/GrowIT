using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class ClientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    
    // Helpful extra info for the grid
    public LifePhase LifePhase { get; set; }
    public int StabilityScore { get; set; }
}