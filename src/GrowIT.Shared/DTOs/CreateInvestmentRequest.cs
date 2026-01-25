namespace GrowIT.Shared.DTOs;

public class CreateInvestmentRequest
{
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; } 
    public Guid FundId { get; set; }
    public Guid ProgramId { get; set; }
    public decimal Amount { get; set; }
    public string PayeeName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}