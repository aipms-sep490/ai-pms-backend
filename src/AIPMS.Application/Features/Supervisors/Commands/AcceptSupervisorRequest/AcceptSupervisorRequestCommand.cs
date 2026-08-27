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

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Locking the supervisor row serializes capacity decisions across different projects.
            var supervisorProfile = await supervisorRepository.GetProfileByUserIdForUpdateAsync(currentUserId.Value, cancellationToken);
            if (supervisorProfile == null)
            {
                throw new ForbiddenException("You do not have a supervisor profile.");
            }

            var supervisorRequest = await requestRepository.GetByIdForUpdateAsync(request.Id, cancellationToken);
            if (supervisorRequest == null)
            {
                throw new NotFoundException("SupervisorRequest", request.Id);
            }

            if (supervisorRequest.SupervisorProfileId != supervisorProfile.Id)
            {
                throw new ForbiddenException("You are not authorized to process this request.");
            }

            if (supervisorRequest.Status == "ACCEPTED")
            {
                var existing = await assignmentRepository.GetByRequestIdAsync(supervisorRequest.Id, cancellationToken);
                if (existing != null && existing.ProjectId == supervisorRequest.ProjectId &&
                    existing.SupervisorProfileId == supervisorProfile.Id)
                {
                    await requestRepository.ActivateProjectAsync(supervisorRequest.ProjectId, cancellationToken);
                    await requestRepository.InitializeProjectWorkspaceAsync(supervisorRequest.ProjectId, currentUserId.Value, cancellationToken);
                    await unitOfWork.CommitAsync(cancellationToken);
                    return Unit.Value;
                }
            }

            if (supervisorRequest.Status != "PENDING")
            {
                throw new ConflictException($"Cannot accept a request that is in status '{supervisorRequest.Status}'. It must be PENDING.");
            }

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
            await requestRepository.InitializeProjectWorkspaceAsync(supervisorRequest.ProjectId, currentUserId.Value, cancellationToken);

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
