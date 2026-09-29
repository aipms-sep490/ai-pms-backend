using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorCandidateRepository
{
    Task<SupervisorCandidateProject?> GetProjectAsync(long projectId, DateTime now, CancellationToken ct);
    Task<IReadOnlyList<SupervisorSelectionPolicy>> GetSelectionPoliciesAsync(
        long academicSemesterId, DateTime now, CancellationToken ct, bool execution = false);
    Task<PagedResult<SupervisorCandidateModel>> SearchAsync(SupervisorCandidateSearch search, CancellationToken ct);
}
