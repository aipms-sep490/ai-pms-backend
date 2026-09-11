using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Domain.Supervisors;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Queries;

public sealed record GetSupervisorCandidatesQuery(long ProjectId, string? Search = null,
    string? Expertise = null, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<SupervisorCandidateDto>>;

public sealed class GetSupervisorCandidatesQueryHandler(ISupervisorCandidateRepository repository,
    SupervisorAccessService access, IProjectAccessService projectAccess, TimeProvider clock)
    : IRequestHandler<GetSupervisorCandidatesQuery, PagedResult<SupervisorCandidateDto>>
{
    public async Task<PagedResult<SupervisorCandidateDto>> Handle(GetSupervisorCandidatesQuery request, CancellationToken ct)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        if ((!actor.Roles.Contains(AppRoles.Admin) && !actor.HasActiveAcademicScope)
            || !await projectAccess.CanAccessAsync(actor.UserId, request.ProjectId, ct))
            throw new ForbiddenException("You cannot view supervisor candidates for this project.");

        var now = clock.GetUtcNow().UtcDateTime;
        var project = await repository.GetProjectAsync(request.ProjectId, now, ct)
            ?? throw new NotFoundException("Project", request.ProjectId);
        if (project.Status != "APPROVED" || project.HasActiveAssignment)
            throw new ConflictException("Supervisor selection requires an approved project without an active assignment.");
        if (!project.HasActiveSemester || project.DepartmentIds.Count == 0)
            throw new ConflictException("The project must have an active semester and active majors in its organization.");

        var policies = await repository.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, ct);
        if (policies.Count != 1 || policies[0].MaxProjectsPerSupervisor is not > 0)
            throw new ConflictException("One active supervisor-selection period with a configured quota is required.");
        var policy = policies[0];
        var limit = policy.MaxProjectsPerSupervisor!.Value;
        var result = await repository.SearchAsync(new SupervisorCandidateSearch(project.Id,
            project.AcademicSemesterId, project.DepartmentIds, limit, request.Search?.Trim(),
            request.Expertise?.Trim(), request.Page, request.PageSize), ct);
        return new(result.Items.Select(candidate =>
        {
            var profile = candidate.Profile.ToDto();
            var capacity = new SupervisorCapacity(candidate.ProfileLimit, limit,
                candidate.ActiveProjects, candidate.SemesterActiveProjects);
            return new SupervisorCandidateDto(profile.Id, profile.UserId, profile.FullName,
                profile.DepartmentId, profile.DepartmentName, profile.Bio, profile.Expertise,
                candidate.ActiveProjects, candidate.SemesterActiveProjects, candidate.ProfileLimit,
                limit, capacity.RemainingSlots, policy.PeriodId);
        }).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }
}
