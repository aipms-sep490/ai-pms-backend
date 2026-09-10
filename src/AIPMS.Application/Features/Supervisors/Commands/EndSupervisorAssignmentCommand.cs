using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands;

public sealed record EndSupervisorAssignmentCommand(long AssignmentId, string Reason) : IRequest<SupervisorAssignmentDto>;

public sealed class EndSupervisorAssignmentCommandHandler(SupervisorAssignmentWorkflow workflow)
    : IRequestHandler<EndSupervisorAssignmentCommand, SupervisorAssignmentDto>
{
    public Task<SupervisorAssignmentDto> Handle(EndSupervisorAssignmentCommand request, CancellationToken ct) =>
        workflow.EndAsync(request.AssignmentId, request.Reason, ct);
}
