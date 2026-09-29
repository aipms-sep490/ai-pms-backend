using AIPMS.Application.Features.Supervisors.DTOs;

namespace AIPMS.Application.Features.Supervisors.Abstractions;

public interface ISupervisorReplacementService
{
    Task<SupervisorAssignmentDto> ReplaceAsync(long assignmentId, long supervisorProfileId, string reason, CancellationToken ct);
}
