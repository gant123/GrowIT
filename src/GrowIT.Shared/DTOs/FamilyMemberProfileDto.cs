namespace GrowIT.Shared.DTOs;

public class FamilyMemberProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public int Age { get; set; }
    public string School { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public decimal TotalInvested { get; set; } // Money spent specifically on THIS person
    public List<TimelineItemDto> Timeline { get; set; } = new();
}