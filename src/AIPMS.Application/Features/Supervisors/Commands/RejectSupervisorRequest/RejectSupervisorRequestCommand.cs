using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.RejectSupervisorRequest;

public sealed record RejectSupervisorRequestCommand(
    long Id,
    string? ResponseMessage) : IRequest<Unit>;

public sealed class RejectSupervisorRequestCommandHandler(
    ICurrentUser currentUser,
    ISupervisorRepository supervisorRepository,
    ISupervisorRequestRepository requestRepository) : IRequestHandler<RejectSupervisorRequestCommand, Unit>
{
    public async Task<Unit> Handle(
        RejectSupervisorRequestCommand request,
        CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId;
        if (currentUserId == null)
        {
            throw new ForbiddenException("User is not authenticated.");
        }

        var supervisorProfile = await supervisorRepository.GetProfileByUserIdAsync(currentUserId.Value, cancellationToken);
        if (supervisorProfile == null)
        {
            throw new ForbiddenException("You do not have a supervisor profile.");
        }

        var supervisorRequest = await requestRepository.GetByIdAsync(request.Id, cancellationToken);
        if (supervisorRequest == null)
        {
            throw new NotFoundException("SupervisorRequest", request.Id);
        }

        if (supervisorRequest.Status != "PENDING")
        {
            throw new ConflictException($"Cannot reject a request that is in status '{supervisorRequest.Status}'. It must be PENDING.");
        }

        if (supervisorRequest.SupervisorProfileId != supervisorProfile.Id)
        {
            throw new ForbiddenException("You are not authorized to process this request.");
        }

        supervisorRequest.Status = "REJECTED";
        supervisorRequest.ResponseMessage = request.ResponseMessage;
        supervisorRequest.RespondedAt = DateTime.UtcNow;

        await requestRepository.UpdateAsync(supervisorRequest, cancellationToken);

        return Unit.Value;
    }
}
