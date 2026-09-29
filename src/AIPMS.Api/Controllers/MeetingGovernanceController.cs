using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1/meetings/{meetingId:long}")]
[ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
public sealed class MeetingGovernanceController(IMeetingGovernanceService service) : ControllerBase
{
    [HttpGet("decisions")]
    [ProducesResponseType<PagedResult<MeetingDecisionDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<MeetingDecisionDto>> Decisions(long meetingId, CancellationToken ct, int page = 1, int pageSize = 20) => service.Decisions(meetingId, page, pageSize, ct);
    [HttpGet("action-items")]
    [ProducesResponseType<PagedResult<MeetingActionItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<MeetingActionItemDto>> Actions(long meetingId, CancellationToken ct, int page = 1, int pageSize = 20) => service.Actions(meetingId, page, pageSize, ct);
    [HttpPost("decisions")]
    [ProducesResponseType<MeetingDecisionDto>(StatusCodes.Status200OK)]
    public Task<MeetingDecisionDto> Decide(long meetingId, CreateMeetingDecisionRequest input, CancellationToken ct) => service.Decide(meetingId, input, ct);
    [HttpPost("action-items")]
    [ProducesResponseType<MeetingActionItemDto>(StatusCodes.Status200OK)]
    public Task<MeetingActionItemDto> CreateAction(long meetingId, SaveMeetingActionItemRequest input, CancellationToken ct) => service.SaveAction(meetingId, null, input, ct);
    [HttpPut("action-items/{id:long}"), HttpPatch("action-items/{id:long}")]
    [ProducesResponseType<MeetingActionItemDto>(StatusCodes.Status200OK)]
    public Task<MeetingActionItemDto> UpdateAction(long meetingId, long id, SaveMeetingActionItemRequest input, CancellationToken ct) => service.SaveAction(meetingId, id, input, ct);
}
