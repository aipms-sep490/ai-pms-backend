namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorAssignmentDto(long Id, long ProjectId, long SupervisorProfileId,
    long SupervisorUserId, string SupervisorName, long SupervisorRequestId, bool IsPrimary,
    DateTime AssignedAt, DateTime? EndedAt, string AssignmentType = "PRIMARY", long? MajorId = null);

public sealed record EndSupervisorAssignmentRequest(string Reason);
