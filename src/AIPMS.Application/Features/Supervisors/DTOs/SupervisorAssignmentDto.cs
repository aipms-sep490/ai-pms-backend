using System;

namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorAssignmentDto(
    long Id,
    long ProjectId,
    long SupervisorProfileId,
    long SupervisorRequestId,
    bool IsPrimary,
    DateTime AssignedAt,
    DateTime? EndedAt);
