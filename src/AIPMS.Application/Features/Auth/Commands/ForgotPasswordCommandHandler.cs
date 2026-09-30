using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed class ForgotPasswordCommandHandler(IPasswordRecoveryQueue queue)
    : IRequestHandler<ForgotPasswordCommand, MessageResponse>
{
    public async Task<MessageResponse> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        await queue.EnqueueAsync(request.Email.Trim(), cancellationToken);
        return new MessageResponse("Request accepted. If an eligible account exists, check your email for password reset instructions.");
    }
}
