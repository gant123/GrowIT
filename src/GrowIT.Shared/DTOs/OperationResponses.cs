namespace GrowIT.Shared.DTOs;

public class EntityCreatedResponse
{
    public string Message { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid FundId { get; set; }
    public Guid ProgramId { get; set; }
    public Guid InvestmentId { get; set; }
}

public class InvestmentActionResponse
{
    public string Message { get; set; } = string.Empty;
    public decimal? NewFundBalance { get; set; }
    public Guid? InvestmentId { get; set; }
}
