using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Domain.Exceptions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands.SendSupervisorRequest;

public sealed record SendSupervisorRequestCommand(
    long ProjectId,
    long SupervisorId,
    string? RequestMessage) : IRequest<SupervisorRequestDto>;

public sealed class SendSupervisorRequestCommandHandler(
    ICurrentUser currentUser,
    IProjectAccessService projectAccessService,
    ISupervisorRepository supervisorRepository,
    ISupervisorRequestRepository requestRepository,
    ISupervisorAssignmentRepository assignmentRepository) : IRequestHandler<SendSupervisorRequestCommand, SupervisorRequestDto>
{
    public async Task<SupervisorRequestDto> Handle(
        SendSupervisorRequestCommand request,
        CancellationToken cancellationToken)
    {
        var currentUserId = currentUser.UserId;
        if (currentUserId == null)
        {
            throw new ForbiddenException("User is not authenticated.");
        }

        var projectExists = await requestRepository.ProjectExistsAsync(request.ProjectId, cancellationToken);
        if (!projectExists)
        {
            throw new NotFoundException("Project", request.ProjectId);
        }

        if (!await projectAccessService.CanAccessAsync(currentUserId.Value, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You are not authorized to send a supervisor request for this project.");
        }

        if (!await requestRepository.IsProjectApprovedAsync(request.ProjectId, cancellationToken))
        {
            throw new ConflictException("Supervisor requests can only be sent for APPROVED projects.");
        }

        var supervisor = await supervisorRepository.GetProfileByIdAsync(request.SupervisorId, cancellationToken);
        if (supervisor == null)
        {
            throw new NotFoundException("SupervisorProfile", request.SupervisorId);
        }

        if (!supervisor.IsAvailable)
        {
            throw new DomainException($"Supervisor with profile ID {request.SupervisorId} is not available.");
        }

        if (supervisor.MaxActiveProjects.HasValue &&
            await assignmentRepository.CountActiveAssignmentsAsync(supervisor.Id, cancellationToken) >= supervisor.MaxActiveProjects.Value)
        {
            throw new ConflictException("The selected supervisor has reached their maximum active projects limit.");
        }

        var hasPending = await requestRepository.HasPendingRequestAsync(request.ProjectId, request.SupervisorId, cancellationToken);
        if (hasPending)
        {
            throw new ConflictException("A pending supervisor request already exists for this project and supervisor combination.");
        }

        var supervisorRequest = new SupervisorRequest
        {
            ProjectId = request.ProjectId,
            SupervisorProfileId = request.SupervisorId,
            RequestedBy = currentUserId.Value,
            Status = "PENDING",
            RequestMessage = request.RequestMessage,
            RequestedAt = DateTime.UtcNow
        };

        await requestRepository.AddAsync(supervisorRequest, cancellationToken);

        return new SupervisorRequestDto(
            supervisorRequest.Id,
            supervisorRequest.ProjectId,
            supervisorRequest.SupervisorProfileId,
            supervisorRequest.RequestedBy,
            supervisorRequest.Status,
            supervisorRequest.RequestMessage,
            supervisorRequest.ResponseMessage,
            supervisorRequest.RequestedAt,
            supervisorRequest.RespondedAt
        );
    }
}
