namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorAssignmentDto(long Id, long ProjectId, long SupervisorProfileId,
    long SupervisorUserId, string SupervisorName, long SupervisorRequestId, bool IsPrimary,
    DateTime AssignedAt, DateTime? EndedAt, string AssignmentType = "PRIMARY", long? MajorId = null, long? AssignedBy = null,
    long? EndedBy = null, string? EndReason = null, long? ReplacesAssignmentId = null,
    IReadOnlyList<SupervisorActionCapabilityDto>? AllowedActions = null,
    IReadOnlyList<string>? Reasons = null)
{
    public string Status => EndedAt.HasValue ? "ENDED" : "ACTIVE";
}

public sealed record SupervisorActionCapabilityDto(string Code, bool Allowed);

public sealed record EndSupervisorAssignmentRequest(string Reason);
