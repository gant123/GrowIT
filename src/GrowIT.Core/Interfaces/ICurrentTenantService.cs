namespace GrowIT.Core.Interfaces;

public interface ICurrentTenantService
{
    Guid? TenantId { get; }
    string? ConnectionString { get; }
}