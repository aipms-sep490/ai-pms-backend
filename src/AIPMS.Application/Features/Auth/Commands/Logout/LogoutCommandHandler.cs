using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Auth.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.Logout;

public sealed class LogoutCommandHandler(
    IAuthRepository repository,
    IOpaqueTokenService opaqueTokenService,
    IRequestContext requestContext,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = opaqueTokenService.Hash(request.RefreshToken);
        var session = await repository.FindRefreshSessionAsync(tokenHash, cancellationToken);
        await repository.RevokeRefreshTokenAsync(
            tokenHash,
            timeProvider.GetUtcNow().UtcDateTime,
            requestContext.IpAddress,
            cancellationToken);

        if (session is not null)
        {
            await auditTrail.RecordAsync(
                new AuditEntry(
                    session.Account.Id,
                    "AUTH_LOGOUT",
                    "USER",
                    session.Account.Id,
                    new Dictionary<string, object?>()),
                cancellationToken);
        }
    }
}
