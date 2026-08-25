using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(
    IAuthRepository repository,
    IOpaqueTokenService opaqueTokenService,
    IPasswordHashingService passwordHashingService,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(
        ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var session = await repository.FindPasswordResetSessionAsync(
            opaqueTokenService.Hash(request.Token),
            cancellationToken);

        if (session is null || session.UsedAtUtc is not null || session.ExpiresAtUtc <= utcNow)
        {
            throw new UnauthorizedException("The password reset token is invalid or expired.");
        }

        await repository.CompletePasswordResetAsync(
            session.Id,
            session.UserId,
            passwordHashingService.Hash(request.NewPassword),
            utcNow,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                session.UserId,
                "AUTH_PASSWORD_RESET_COMPLETED",
                "USER",
                session.UserId,
                new Dictionary<string, object?>()),
            cancellationToken);
    }
}
