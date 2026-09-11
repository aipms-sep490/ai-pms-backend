using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands;

public sealed record SendSupervisorRequestCommand(long ProjectId, long SupervisorProfileId, string? Message) : IRequest<SupervisorRequestDto>;
public sealed record CancelSupervisorRequestCommand(long RequestId) : IRequest<SupervisorRequestDto>;
public sealed record AcceptSupervisorRequestCommand(long RequestId, string? Message) : IRequest<SupervisorRequestDto>;
public sealed record RejectSupervisorRequestCommand(long RequestId, string? Message) : IRequest<SupervisorRequestDto>;

public sealed class SendSupervisorRequestCommandHandler(SupervisorRequestWorkflow workflow)
    : IRequestHandler<SendSupervisorRequestCommand, SupervisorRequestDto>
{
    public Task<SupervisorRequestDto> Handle(SendSupervisorRequestCommand request, CancellationToken ct) =>
        workflow.SendAsync(request.ProjectId, request.SupervisorProfileId, request.Message, ct);
}
public sealed class CancelSupervisorRequestCommandHandler(SupervisorRequestWorkflow workflow)
    : IRequestHandler<CancelSupervisorRequestCommand, SupervisorRequestDto>
{
    public Task<SupervisorRequestDto> Handle(CancelSupervisorRequestCommand request, CancellationToken ct) =>
        workflow.CancelAsync(request.RequestId, ct);
}
public sealed class AcceptSupervisorRequestCommandHandler(SupervisorRequestWorkflow workflow)
    : IRequestHandler<AcceptSupervisorRequestCommand, SupervisorRequestDto>
{
    public Task<SupervisorRequestDto> Handle(AcceptSupervisorRequestCommand request, CancellationToken ct) =>
        workflow.RespondAsync(request.RequestId, true, request.Message, ct);
}
public sealed class RejectSupervisorRequestCommandHandler(SupervisorRequestWorkflow workflow)
    : IRequestHandler<RejectSupervisorRequestCommand, SupervisorRequestDto>
{
    public Task<SupervisorRequestDto> Handle(RejectSupervisorRequestCommand request, CancellationToken ct) =>
        workflow.RespondAsync(request.RequestId, false, request.Message, ct);
}
