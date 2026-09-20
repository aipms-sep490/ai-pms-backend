using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.Teams.Services;
using MediatR;

namespace AIPMS.Application.Features.Teams.Commands;

public sealed record RequestTeamLeaderChangeCommand(long TeamId, long NewLeaderUserId,
    string? Message) : IRequest<TeamLeaderChangeRequestDto>;
public sealed record RequestOrTransferTeamLeaderCommand(long TeamId, long NewLeaderUserId,
    string? Message) : IRequest<object>;
public sealed record ApproveTeamLeaderChangeCommand(long RequestId, string? Message) : IRequest<TeamLeaderChangeRequestDto>;
public sealed record RejectTeamLeaderChangeCommand(long RequestId, string? Message) : IRequest<TeamLeaderChangeRequestDto>;
public sealed record GetTeamLeaderChangeRequestsQuery(long? TeamId = null, string? Status = null,
    int Page = 1, int PageSize = 20) : IRequest<PagedResult<TeamLeaderChangeRequestDto>>;

public sealed class RequestTeamLeaderChangeCommandHandler(TeamLeaderChangeWorkflow workflow)
    : IRequestHandler<RequestTeamLeaderChangeCommand, TeamLeaderChangeRequestDto>
{
    public Task<TeamLeaderChangeRequestDto> Handle(RequestTeamLeaderChangeCommand request, CancellationToken ct) =>
        workflow.RequestAsync(request, ct);
}

public sealed class RequestOrTransferTeamLeaderCommandHandler(TeamLeaderChangeWorkflow workflow)
    : IRequestHandler<RequestOrTransferTeamLeaderCommand, object>
{
    public Task<object> Handle(RequestOrTransferTeamLeaderCommand request, CancellationToken ct) =>
        workflow.RequestOrTransferAsync(request, ct);
}


public sealed class ApproveTeamLeaderChangeCommandHandler(TeamLeaderChangeWorkflow workflow)
    : IRequestHandler<ApproveTeamLeaderChangeCommand, TeamLeaderChangeRequestDto>
{
    public Task<TeamLeaderChangeRequestDto> Handle(ApproveTeamLeaderChangeCommand request, CancellationToken ct) =>
        workflow.RespondAsync(request.RequestId, true, request.Message, ct);
}

public sealed class RejectTeamLeaderChangeCommandHandler(TeamLeaderChangeWorkflow workflow)
    : IRequestHandler<RejectTeamLeaderChangeCommand, TeamLeaderChangeRequestDto>
{
    public Task<TeamLeaderChangeRequestDto> Handle(RejectTeamLeaderChangeCommand request, CancellationToken ct) =>
        workflow.RespondAsync(request.RequestId, false, request.Message, ct);
}

public sealed class GetTeamLeaderChangeRequestsQueryHandler(TeamLeaderChangeWorkflow workflow)
    : IRequestHandler<GetTeamLeaderChangeRequestsQuery, PagedResult<TeamLeaderChangeRequestDto>>
{
    public Task<PagedResult<TeamLeaderChangeRequestDto>> Handle(GetTeamLeaderChangeRequestsQuery request, CancellationToken ct) =>
        workflow.ListAsync(request.TeamId, request.Status, request.Page, request.PageSize, ct);
}
