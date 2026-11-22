using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class ImprintListDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ImprintCategory Category { get; set; }
    public ImpactOutcome Outcome { get; set; }
    public DateTime Date { get; set; }
    public DateTime? FollowupDate { get; set; }

    /// <summary>
    /// Outcomes have no stored verification yet, so this is always false. (It used to be
    /// hard-coded to true, which labelled every new outcome "Verified".)
    /// </summary>
    public bool IsVerified { get; set; }
}
