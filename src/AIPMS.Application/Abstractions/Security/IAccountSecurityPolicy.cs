namespace AIPMS.Application.Abstractions.Security;

public interface IAccountSecurityPolicy
{
    int FailedLoginThreshold { get; }

    int LockoutMinutes { get; }

    int RefreshTokenDays { get; }

    int PasswordResetMinutes { get; }
}
