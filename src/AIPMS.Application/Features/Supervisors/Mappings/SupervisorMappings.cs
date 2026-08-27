using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;

namespace AIPMS.Application.Features.Supervisors.Mappings;

public static class SupervisorMappings
{
    public static SupervisorRequestDto ToDto(this SupervisorRequest request) => new(
        request.Id,
        request.ProjectId,
        request.SupervisorProfileId,
        request.RequestedBy,
        request.Status,
        request.RequestMessage,
        request.ResponseMessage,
        request.RequestedAt,
        request.RespondedAt);
}
