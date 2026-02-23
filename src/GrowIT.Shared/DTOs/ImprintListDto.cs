using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class ImprintListDto
{
    public Guid Id { get; set; }
    public Guid PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public ImprintCategory Category { get; set; }
    public DateTime Date { get; set; }
    public bool IsVerified { get; set; }
}
