using AIPMS.Application.Features.Supervisors.DTOs;

namespace AIPMS.Application.Features.Supervisors.Models;

public sealed record SupervisorAssignmentModel(long Id, long ProjectId, long SupervisorProfileId,
    long SupervisorUserId, string SupervisorName, long SupervisorRequestId, bool IsPrimary,
    DateTime AssignedAt, DateTime? EndedAt, string ProjectStatus)
{
    public SupervisorAssignmentDto ToDto() => new(Id, ProjectId, SupervisorProfileId,
        SupervisorUserId, SupervisorName, SupervisorRequestId, IsPrimary, AssignedAt, EndedAt);
}

public sealed record SupervisorAssignmentSearch(long? ProjectId, long? SupervisorUserId,
    string? Status, int Page, int PageSize);
