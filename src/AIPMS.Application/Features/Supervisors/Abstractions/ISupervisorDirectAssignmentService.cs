using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public sealed record DirectAssignSupervisorRequest(
    long SupervisorProfileId,
    string AssignmentType,
    long? MajorId,
    string Reason);

public interface ISupervisorDirectAssignmentService
{
    Task<SupervisorAssignmentDto> DirectAssignAsync(
        long projectId,
        long supervisorProfileId,
        string assignmentType,
        long? majorId,
        string reason,
        CancellationToken ct = default);

    Task<PagedResult<SupervisorCandidateDto>> GetDirectCandidatesAsync(
        long projectId,
        string assignmentType,
        long? majorId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
