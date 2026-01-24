namespace GrowIT.Core.Interfaces;

public interface IMustHaveTenant
{
    public Guid TenantId { get; set; }
}