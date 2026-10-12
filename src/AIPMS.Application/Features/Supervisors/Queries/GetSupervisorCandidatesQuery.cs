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
    string? Expertise = null, int Page = 1, int PageSize = 20,
    string AssignmentType = "PRIMARY", long? MajorId = null)
    : IRequest<PagedResult<SupervisorCandidateDto>>;

public sealed class GetSupervisorCandidatesQueryHandler(ISupervisorCandidateRepository repository,
    SupervisorAccessService access, IProjectAccessService projectAccess, TimeProvider clock)
    : IRequestHandler<GetSupervisorCandidatesQuery, PagedResult<SupervisorCandidateDto>>
{
    public async Task<PagedResult<SupervisorCandidateDto>> Handle(GetSupervisorCandidatesQuery request, CancellationToken ct)
    {
        var selection = await new SupervisorCandidatePolicy(repository, access, projectAccess, clock).Select(request, ct);
        var project = selection.Project;
        var policy = selection.Policy;
        var limit = policy.MaxProjectsPerSupervisor!.Value;
        var result = await repository.SearchAsync(new SupervisorCandidateSearch(project.Id,
            project.AcademicSemesterId, project.DepartmentIds, limit, request.Search?.Trim(),
            request.Expertise?.Trim(), request.Page, request.PageSize,
            request.AssignmentType == "DISCIPLINE_MENTOR" ? "DISCIPLINE_MENTOR" : "PRIMARY", request.MajorId), ct);
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
