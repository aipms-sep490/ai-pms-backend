using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Abstractions;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands;

public sealed record ReplaceSupervisorAssignmentRequest(long SupervisorProfileId, string Reason);
public sealed record ReplaceSupervisorAssignmentCommand(long AssignmentId, long SupervisorProfileId, string Reason)
    : IRequest<SupervisorAssignmentDto>;

public sealed class ReplaceSupervisorAssignmentCommandHandler(ISupervisorReplacementService service)
    : IRequestHandler<ReplaceSupervisorAssignmentCommand, SupervisorAssignmentDto>
{
    public Task<SupervisorAssignmentDto> Handle(ReplaceSupervisorAssignmentCommand request, CancellationToken ct) =>
        service.ReplaceAsync(request.AssignmentId, request.SupervisorProfileId, request.Reason, ct);
}
