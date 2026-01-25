namespace GrowIT.Core.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
}