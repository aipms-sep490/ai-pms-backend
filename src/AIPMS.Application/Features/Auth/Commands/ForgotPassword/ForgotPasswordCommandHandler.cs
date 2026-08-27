using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Email;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.ForgotPassword;

public sealed class ForgotPasswordCommandHandler(
    IAuthRepository repository,
    IOpaqueTokenService opaqueTokenService,
    IPasswordResetNotifier notifier,
    IAccountSecurityPolicy securityPolicy,
    IRequestContext requestContext,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : IRequestHandler<ForgotPasswordCommand, MessageResponse>
{
    private const string GenericMessage =
        "If an eligible account exists, password reset instructions have been sent.";

    public async Task<MessageResponse> Handle(
        ForgotPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var account = await repository.FindByEmailAsync(
            request.Email.Trim(),
            cancellationToken);

        if (account is null
            || !string.Equals(account.Status, "ACTIVE", StringComparison.Ordinal))
        {
            return new MessageResponse(GenericMessage);
        }

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = utcNow.AddMinutes(securityPolicy.PasswordResetMinutes);
        var token = opaqueTokenService.Generate();

        await repository.CreatePasswordResetTokenAsync(
            account.Id,
            token.Hash,
            expiresAtUtc,
            utcNow,
            requestContext.IpAddress,
            cancellationToken);

        await notifier.SendAsync(
            account.Email,
            account.FullName,
            token.Value,
            expiresAtUtc,
            cancellationToken);

        await auditTrail.RecordAsync(
            new AuditEntry(
                account.Id,
                "AUTH_PASSWORD_RESET_REQUESTED",
                "USER",
                account.Id,
                new Dictionary<string, object?>()),
            cancellationToken);

        return new MessageResponse(GenericMessage);
    }
}
