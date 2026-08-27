using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest;

public sealed record CancelSupervisorRequestCommand(long Id) : IRequest<Unit>;

public sealed class CancelSupervisorRequestCommandHandler(
    ICurrentUser currentUser,
    ISupervisorRequestRepository requestRepository) : IRequestHandler<CancelSupervisorRequestCommand, Unit>
{
    public async Task<Unit> Handle(CancelSupervisorRequestCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("User is not authenticated.");
        var supervisorRequest = await requestRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("SupervisorRequest", request.Id);

        if (supervisorRequest.Status != "PENDING")
        {
            throw new ConflictException("Only PENDING supervisor requests can be cancelled.");
        }

        // The request row is retained as the audit history: RequestedBy identifies the
        // cancellation actor and RespondedAt records when the cancellation happened.
        if (supervisorRequest.RequestedBy != userId)
        {
            throw new ForbiddenException("Only the user who sent this request can cancel it.");
        }

        supervisorRequest.Status = "CANCELLED";
        supervisorRequest.RespondedAt = DateTime.UtcNow;
        await requestRepository.UpdateAsync(supervisorRequest, cancellationToken);
        return Unit.Value;
    }
}
