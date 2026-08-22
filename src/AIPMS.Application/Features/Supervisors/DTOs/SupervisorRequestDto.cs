using System;

namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SupervisorRequestDto(
    long Id,
    long ProjectId,
    long SupervisorProfileId,
    long RequestedBy,
    string Status,
    string? RequestMessage,
    string? ResponseMessage,
    DateTime RequestedAt,
    DateTime? RespondedAt);
