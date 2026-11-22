using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class InvestmentListDto
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string PersonName { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>Name of the money source that paid for it.</summary>
    public string FundName { get; set; } = "";
    public decimal Amount { get; set; }
    public InvestmentStatus Status { get; set; }
}
