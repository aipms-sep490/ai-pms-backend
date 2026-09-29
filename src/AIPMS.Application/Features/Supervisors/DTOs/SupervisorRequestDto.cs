using AIPMS.Application.Features.Supervisors.Models;

namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed record SendSupervisorRequest(long SupervisorProfileId, string? Message,
    string AssignmentType = "PRIMARY", long? MajorId = null);
public sealed record RespondToSupervisorRequest(string? Message);
public sealed record SupervisorRequestDto(long Id, long ProjectId, long SupervisorProfileId,
    long RequestedBy, string Status, string? RequestMessage, string? ResponseMessage,
    DateTime RequestedAt, DateTime? RespondedAt, long? AssignmentId,
    string AssignmentType = "PRIMARY", long? MajorId = null);

internal static class SupervisorRequestDtoMapper
{
    public static SupervisorRequestDto ToDto(this SupervisorRequestModel request) =>
        new(request.Id, request.ProjectId, request.SupervisorProfileId, request.RequestedBy,
            request.Status, request.RequestMessage, request.ResponseMessage,
            request.RequestedAt, request.RespondedAt, request.AssignmentId,
            request.AssignmentType, request.MajorId);
}
