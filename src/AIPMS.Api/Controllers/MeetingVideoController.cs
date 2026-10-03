using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/meetings/{meetingId:long}/video")]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(404)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class MeetingVideoController(IMeetingVideoService video) : ControllerBase
{
    [HttpGet("session")]
    [ProducesResponseType<VideoSessionDto>(200)]
    public Task<VideoSessionDto> Session(long meetingId, CancellationToken ct) => video.GetSessionAsync(meetingId, ct);

    [HttpPost("start")]
    [ProducesResponseType<VideoSessionDto>(200)]
    public Task<VideoSessionDto> Start(long meetingId, CancellationToken ct) => video.StartAsync(meetingId, ct);

    [HttpPost("join")]
    [ProducesResponseType<VideoJoinCredentialDto>(200)]
    public Task<VideoJoinCredentialDto> Join(long meetingId, CancellationToken ct) => video.JoinAsync(meetingId, ct);

    [HttpPost("end")]
    [ProducesResponseType<VideoSessionDto>(200)]
    public Task<VideoSessionDto> End(long meetingId, CancellationToken ct) => video.EndAsync(meetingId, ct);

    [HttpGet("presence")]
    [ProducesResponseType<VideoPresenceDto>(200)]
    public Task<VideoPresenceDto> Presence(long meetingId, CancellationToken ct) => video.GetPresenceAsync(meetingId, ct);
}


