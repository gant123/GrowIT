using GrowIT.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace GrowIT.Backend.Services;

public sealed class LegacyCompatiblePasswordHasher : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _identityHasher = new();

    public string HashPassword(User user, string password) =>
        _identityHasher.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        if (hashedPassword.StartsWith("$2", StringComparison.Ordinal))
        {
            return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        return _identityHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
