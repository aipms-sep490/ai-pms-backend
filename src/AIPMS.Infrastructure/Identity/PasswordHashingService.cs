using AIPMS.Application.Abstractions.Security;
using Microsoft.AspNetCore.Identity;

namespace AIPMS.Infrastructure.Identity;

internal sealed class PasswordHashingService : IPasswordHashingService
{
    private static readonly object UserMarker = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string password) =>
        _passwordHasher.HashPassword(UserMarker, password);

    public bool Verify(string passwordHash, string providedPassword) =>
        _passwordHasher.VerifyHashedPassword(UserMarker, passwordHash, providedPassword)
        is not PasswordVerificationResult.Failed;
}
