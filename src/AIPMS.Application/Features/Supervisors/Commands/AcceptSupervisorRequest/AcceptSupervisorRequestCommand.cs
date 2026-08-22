using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Domain.Entities;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.AcceptSupervisorRequest;

public sealed record AcceptSupervisorRequestCommand(long Id) : IRequest<Unit>;

public sealed class AcceptSupervisorRequestCommandHandler(
    ICurrentUser currentUser,
    ISupervisorRepository supervisorRepository,
    ISupervisorRequestRepository requestRepository,
    ISupervisorAssignmentRepository assignmentRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<AcceptSupervisorRequestCommand, Unit>
{
    public async Task<Unit> Handle(
        AcceptSupervisorRequestCommand request,
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
            throw new ConflictException($"Cannot accept a request that is in status '{supervisorRequest.Status}'. It must be PENDING.");
        }

        if (supervisorRequest.SupervisorProfileId != supervisorProfile.Id)
        {
            throw new ForbiddenException("You are not authorized to process this request.");
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await requestRepository.IsProjectApprovedAsync(supervisorRequest.ProjectId, cancellationToken))
            {
                throw new ConflictException("Supervisor requests can only be accepted for APPROVED projects.");
            }

            if (await assignmentRepository.GetActiveAssignmentByProjectAsync(supervisorRequest.ProjectId, cancellationToken) != null)
            {
                throw new ConflictException("The project already has an active supervisor assignment.");
            }

            if (supervisorProfile.MaxActiveProjects.HasValue)
            {
                var activeCount = await assignmentRepository.CountActiveAssignmentsAsync(supervisorProfile.Id, cancellationToken);
                if (activeCount >= supervisorProfile.MaxActiveProjects.Value)
                {
                    throw new ConflictException($"Cannot accept request. You have reached your maximum active projects limit ({supervisorProfile.MaxActiveProjects.Value}).");
                }
            }

            supervisorRequest.Status = "ACCEPTED";
            supervisorRequest.RespondedAt = DateTime.UtcNow;
            await requestRepository.UpdateAsync(supervisorRequest, cancellationToken);

            var assignment = new SupervisorAssignment
            {
                ProjectId = supervisorRequest.ProjectId,
                SupervisorProfileId = supervisorRequest.SupervisorProfileId,
                SupervisorRequestId = supervisorRequest.Id,
                IsPrimary = true,
                AssignedAt = DateTime.UtcNow
            };

            await assignmentRepository.AddAsync(assignment, cancellationToken);
            await requestRepository.ActivateProjectAsync(supervisorRequest.ProjectId, cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        return Unit.Value;
    }
}
