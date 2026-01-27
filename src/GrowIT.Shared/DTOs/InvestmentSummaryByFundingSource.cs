namespace GrowIT.Shared.DTOs;

public class InvestmentSummaryByFundingSource
{
    public string FundingSource { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal UtilizationPercentage { get; set; }
}
