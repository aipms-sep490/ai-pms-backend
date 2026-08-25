using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Models;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IAuthRepository authRepository,
    IPasswordHashingService passwordHashingService,
    IAccessTokenService accessTokenService,
    IOpaqueTokenService opaqueTokenService,
    IAccountSecurityPolicy securityPolicy,
    IRequestContext requestContext,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var account = await authRepository.FindByEmailAsync(
            request.Email.Trim(),
            cancellationToken);

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        if (account?.LockoutEndAt is not null && account.LockoutEndAt > utcNow)
        {
            await RecordLoginAuditAsync(account.Id, "DENIED", cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (account is null
            || !passwordHashingService.Verify(account.PasswordHash, request.Password))
        {
            if (account is not null)
            {
                var failedCount = account.AccessFailedCount + 1;
                var lockoutEndAt = failedCount >= securityPolicy.FailedLoginThreshold
                    ? utcNow.AddMinutes(securityPolicy.LockoutMinutes)
                    : (DateTime?)null;

                await authRepository.RecordFailedLoginAsync(
                    account.Id,
                    failedCount,
                    lockoutEndAt,
                    utcNow,
                    cancellationToken);
            }

            await RecordLoginAuditAsync(account?.Id, "FAILURE", cancellationToken);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!string.Equals(account.Status, "ACTIVE", StringComparison.Ordinal))
        {
            await RecordLoginAuditAsync(account.Id, "DENIED", cancellationToken);
            throw new ForbiddenException("This account is not active.");
        }

        var accessToken = accessTokenService.Create(new AccessTokenDescriptor(
            account.Id,
            account.Email,
            account.FullName,
            account.Roles,
            account.PasswordChangedAt));

        var refreshToken = opaqueTokenService.Generate();
        var refreshTokenExpiresAtUtc = utcNow.AddDays(securityPolicy.RefreshTokenDays);

        await authRepository.CompleteSuccessfulLoginAsync(
            account.Id,
            utcNow,
            new RefreshTokenData(
                refreshToken.Hash,
                Guid.NewGuid(),
                refreshTokenExpiresAtUtc,
                requestContext.IpAddress,
                requestContext.UserAgent),
            cancellationToken);

        await RecordLoginAuditAsync(account.Id, "SUCCESS", cancellationToken);

        return new LoginResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            refreshToken.Value,
            refreshTokenExpiresAtUtc,
            new AuthUserDto(
                account.Id,
                account.Email,
                account.FullName,
                account.Roles));
    }

    private Task RecordLoginAuditAsync(
        long? userId,
        string outcome,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditEntry(
                userId,
                "AUTH_LOGIN",
                "USER",
                userId,
                new Dictionary<string, object?>(),
                outcome),
            cancellationToken);
}
