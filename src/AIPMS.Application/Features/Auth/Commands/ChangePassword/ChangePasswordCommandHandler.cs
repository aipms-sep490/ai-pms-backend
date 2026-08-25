using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    IAuthRepository repository,
    IPasswordHashingService passwordHashingService,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await repository.FindByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();

        if (!passwordHashingService.Verify(account.PasswordHash, request.CurrentPassword))
        {
            throw new UnauthorizedException("The current password is incorrect.");
        }

        if (passwordHashingService.Verify(account.PasswordHash, request.NewPassword))
        {
            throw new ConflictException("The new password must differ from the current password.");
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdatePasswordAsync(
            userId,
            passwordHashingService.Hash(request.NewPassword),
            utcNow,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                userId,
                "AUTH_PASSWORD_CHANGED",
                "USER",
                userId,
                new Dictionary<string, object?>()),
            cancellationToken);
    }
}
