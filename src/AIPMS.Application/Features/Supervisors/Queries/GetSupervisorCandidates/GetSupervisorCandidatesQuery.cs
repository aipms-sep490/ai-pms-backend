using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries.GetSupervisorCandidates;

public sealed record GetSupervisorCandidatesQuery(long ProjectId, string? Expertise) : IRequest<IReadOnlyList<SupervisorCandidateDto>>;

public sealed class GetSupervisorCandidatesQueryHandler(
    ICurrentUser currentUser,
    IProjectAccessService projectAccessService,
    ISupervisorRequestRepository requestRepository,
    ISupervisorRepository supervisorRepository)
    : IRequestHandler<GetSupervisorCandidatesQuery, IReadOnlyList<SupervisorCandidateDto>>
{
    public async Task<IReadOnlyList<SupervisorCandidateDto>> Handle(GetSupervisorCandidatesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("User is not authenticated.");
        if (!await projectAccessService.CanAccessAsync(userId, request.ProjectId, cancellationToken))
            throw new ForbiddenException("You are not authorized to view supervisor candidates for this project.");
        if (!await requestRepository.ProjectExistsAsync(request.ProjectId, cancellationToken))
            throw new NotFoundException("Project", request.ProjectId);
        if (!await requestRepository.IsProjectApprovedAsync(request.ProjectId, cancellationToken))
            throw new ConflictException("Supervisor candidates are available only for APPROVED projects.");

        return await supervisorRepository.GetEligibleCandidatesAsync(request.Expertise, cancellationToken);
    }
}
