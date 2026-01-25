using System.Security.Claims;
using GrowIT.Core.Interfaces;

namespace GrowIT.API.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var idClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            return idClaim != null && Guid.TryParse(idClaim.Value, out var userId) ? userId : null;
        }
    }
}