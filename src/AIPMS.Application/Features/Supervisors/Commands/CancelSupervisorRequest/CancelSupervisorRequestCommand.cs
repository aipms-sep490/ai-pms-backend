using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.CancelSupervisorRequest;

public sealed record CancelSupervisorRequestCommand(long Id) : IRequest<Unit>;

public sealed class CancelSupervisorRequestCommandHandler(
    ICurrentUser currentUser,
    IProjectAccessService projectAccessService,
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

        if (!await projectAccessService.CanAccessAsync(userId, supervisorRequest.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to cancel this supervisor request.");
        }

        supervisorRequest.Status = "CANCELLED";
        supervisorRequest.RespondedAt = DateTime.UtcNow;
        await requestRepository.UpdateAsync(supervisorRequest, cancellationToken);
        return Unit.Value;
    }
}
