using AIPMS.Application.Abstractions.Security;

namespace AIPMS.Infrastructure.Identity.Configuration;

public sealed class AccountSecuritySettings : IAccountSecurityPolicy
{
    public const string SectionName = "AccountSecurity";

    public int FailedLoginThreshold { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    public int PasswordResetMinutes { get; set; } = 30;
}
