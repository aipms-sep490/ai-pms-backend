using AIPMS.Application.Features.Supervisors.DTOs;

namespace AIPMS.Application.Features.Supervisors.Models;

public sealed record SupervisorAssignmentModel(long Id, long ProjectId, long SupervisorProfileId,
    long SupervisorUserId, string SupervisorName, long SupervisorRequestId, bool IsPrimary,
    DateTime AssignedAt, DateTime? EndedAt, string ProjectStatus,
    string AssignmentType = "PRIMARY", long? MajorId = null, long? AssignedBy = null,
    long? EndedBy = null, string? EndReason = null, long? ReplacesAssignmentId = null)
{
    public SupervisorAssignmentDto ToDto() => new(Id, ProjectId, SupervisorProfileId,
        SupervisorUserId, SupervisorName, SupervisorRequestId, IsPrimary, AssignedAt, EndedAt,
        AssignmentType, MajorId, AssignedBy, EndedBy, EndReason, ReplacesAssignmentId);
}

public sealed record SupervisorAssignmentSearch(long? ProjectId, long? SupervisorUserId,
    string? Status, int Page, int PageSize);
