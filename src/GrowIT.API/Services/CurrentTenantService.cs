using System.Security.Claims;
using GrowIT.Core.Interfaces;

namespace GrowIT.API.Services;

public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? TenantId
    {
        get
        {
            // 1. Try to get the user from the current request
            var user = _httpContextAccessor.HttpContext?.User;

            // 2. Look for the "tenantId" claim we put in the JWT
            var claim = user?.FindFirst("tenantId")?.Value;

            // 3. Parse it. If missing, return null (or throw error in strict mode)
            if (Guid.TryParse(claim, out var tenantId))
            {
                return tenantId;
            }

            // FALLBACK: For development only, you might want a default. 
            // In Production, this should probably be null to prevent data leaks.
            return Guid.Empty; 
        }
    }

    public string? ConnectionString => null;
}