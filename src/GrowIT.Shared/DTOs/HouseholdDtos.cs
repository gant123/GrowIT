using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class HouseholdDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? PrimaryClientId { get; set; }
    public string? PrimaryClientName { get; set; }
    public int MemberCount { get; set; }
    public List<HouseholdMemberSummaryDto> Members { get; set; } = new();
}

public class HouseholdMemberSummaryDto
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public HouseholdRole Role { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class CreateHouseholdResponseDto
{
    public Guid HouseholdId { get; set; }
    public string Message { get; set; } = string.Empty;
}
