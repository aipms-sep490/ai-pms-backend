using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Auth.Models;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.RefreshToken;

public sealed class RefreshTokenCommandHandler(
    IAuthRepository repository,
    IOpaqueTokenService opaqueTokenService,
    IAccessTokenService accessTokenService,
    IAccountSecurityPolicy securityPolicy,
    IRequestContext requestContext,
    IAuditTrail auditTrail,
    TimeProvider timeProvider)
    : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = opaqueTokenService.Hash(request.RefreshToken);
        var session = await repository.FindRefreshSessionAsync(tokenHash, cancellationToken)
            ?? throw new UnauthorizedException("The refresh token is invalid.");

        if (session.RevokedAtUtc is not null || session.ReuseDetectedAtUtc is not null)
        {
            await repository.RevokeRefreshTokenFamilyForReuseAsync(
                session.FamilyId,
                utcNow,
                requestContext.IpAddress,
                cancellationToken);

            await RecordAuditAsync(session.Account.Id, "DENIED", cancellationToken);
            throw new UnauthorizedException("The refresh token is invalid.");
        }

        if (session.ExpiresAtUtc <= utcNow)
        {
            await RecordAuditAsync(session.Account.Id, "FAILURE", cancellationToken);
            throw new UnauthorizedException("The refresh token has expired.");
        }

        if (!string.Equals(session.Account.Status, "ACTIVE", StringComparison.Ordinal))
        {
            await RecordAuditAsync(session.Account.Id, "DENIED", cancellationToken);
            throw new ForbiddenException("This account is not active.");
        }

        var replacement = opaqueTokenService.Generate();
        var replacementExpiresAtUtc = utcNow.AddDays(securityPolicy.RefreshTokenDays);

        await repository.RotateRefreshTokenAsync(
            session.Id,
            new RefreshTokenData(
                replacement.Hash,
                session.FamilyId,
                replacementExpiresAtUtc,
                requestContext.IpAddress,
                requestContext.UserAgent),
            utcNow,
            cancellationToken);

        var accessToken = accessTokenService.Create(new AccessTokenDescriptor(
            session.Account.Id,
            session.Account.Email,
            session.Account.FullName,
            session.Account.Roles,
            session.Account.PasswordChangedAt));

        await RecordAuditAsync(session.Account.Id, "SUCCESS", cancellationToken);

        return new LoginResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            replacement.Value,
            replacementExpiresAtUtc,
            new AuthUserDto(
                session.Account.Id,
                session.Account.Email,
                session.Account.FullName,
                session.Account.Roles));
    }

    private Task RecordAuditAsync(
        long userId,
        string outcome,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditEntry(
                userId,
                "AUTH_REFRESH_TOKEN_ROTATED",
                "USER",
                userId,
                new Dictionary<string, object?>(),
                outcome),
            cancellationToken);
}
