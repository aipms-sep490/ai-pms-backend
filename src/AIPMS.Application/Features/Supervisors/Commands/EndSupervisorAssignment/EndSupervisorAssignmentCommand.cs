using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.EndSupervisorAssignment;

public sealed record EndSupervisorAssignmentCommand(long Id) : IRequest<Unit>;

public sealed class EndSupervisorAssignmentCommandHandler(
    ICurrentUser currentUser,
    IProjectAccessService projectAccessService,
    ISupervisorAssignmentRepository assignmentRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<EndSupervisorAssignmentCommand, Unit>
{
    public async Task<Unit> Handle(
        EndSupervisorAssignmentCommand request,
        CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId;
        if (currentUserId == null)
        {
            throw new ForbiddenException("User is not authenticated.");
        }

        var assignment = await assignmentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (assignment == null)
        {
            throw new NotFoundException("SupervisorAssignment", request.Id);
        }

        if (assignment.EndedAt != null)
        {
            throw new ConflictException("The supervisor assignment has already been ended.");
        }

        if (!await projectAccessService.CanAccessAsync(currentUserId.Value, assignment.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to end this supervisor assignment.");
        }

        assignment.EndedAt = DateTime.UtcNow;

        await assignmentRepository.UpdateAsync(assignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
