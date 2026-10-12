using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController, Authorize, Route("api/v1"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404), ProducesResponseType<ProblemDetails>(409)]
public sealed class MeetingCollaborationController(IMeetingCollaborationService service) : ControllerBase
{
    [HttpPost("projects/{projectId:long}/meeting-series"), ProducesResponseType<MeetingSeriesDto>(200)]
    public Task<MeetingSeriesDto> Create(long projectId, CreateMeetingSeriesRequest input, CancellationToken ct) => service.CreateSeries(projectId, input, ct);
    [HttpGet("meetings/{meetingId:long}/collaboration"), ProducesResponseType<MeetingCollaborationDto>(200)]
    public Task<MeetingCollaborationDto> Get(long meetingId, CancellationToken ct) => service.Get(meetingId, ct);
    [HttpPut("meetings/{meetingId:long}/rsvp"), ProducesResponseType<MeetingRsvpDto>(200)]
    public Task<MeetingRsvpDto> Respond(long meetingId, RespondMeetingRequest input, CancellationToken ct) => service.Respond(meetingId, input, ct);
    [HttpPost("meetings/{meetingId:long}/notes/lock"), ProducesResponseType<MeetingCollaborationDto>(200)]
    public Task<MeetingCollaborationDto> Lock(long meetingId, LockMeetingNotesRequest input, CancellationToken ct) => service.LockNotes(meetingId, input, ct);
    [HttpGet("meetings/{meetingId:long}/notes/history"), ProducesResponseType<PagedResult<MeetingNotesVersionDto>>(200)]
    public Task<PagedResult<MeetingNotesVersionDto>> History(long meetingId, CancellationToken ct, int page = 1, int pageSize = 20) => service.NotesHistory(meetingId, page, pageSize, ct);
}
