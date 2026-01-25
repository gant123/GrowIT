namespace GrowIT.Shared.DTOs;

public class FundDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AvailableAmount { get; set; }
}