using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class InvestmentSummaryByCategory
{
    public InvestmentCategory Category { get; set; }
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AverageAmount { get; set; }
}
