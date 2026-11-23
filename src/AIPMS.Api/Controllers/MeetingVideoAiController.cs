using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Video;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/meetings/{meetingId:long}/video/ai")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class MeetingVideoAiController(IMeetingVideoAiService service) : ControllerBase
{
    private const long MaxAudioClipBytes = UploadPolicies.MeetingAudioClipLimit;

    [HttpGet("workspace")]
    [ProducesResponseType<MeetingTranscriptFeedDto>(StatusCodes.Status200OK)]
    public Task<MeetingTranscriptFeedDto> Workspace(
        long meetingId,
        [FromQuery] long? afterSegmentId = null,
        CancellationToken ct = default) =>
        service.GetWorkspaceAsync(meetingId, afterSegmentId, ct);

    [HttpPost("transcript")]
    [ProducesResponseType<MeetingTranscriptSegmentDto>(StatusCodes.Status200OK)]
    public Task<MeetingTranscriptSegmentDto> AppendTranscript(
        long meetingId,
        [FromBody] CreateTranscriptSegmentRequest request,
        CancellationToken ct = default) =>
        service.AppendSegmentAsync(meetingId, request, ct);

    [HttpPost("transcribe-audio")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxAudioClipBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioClipBytes)]
    [ProducesResponseType<MeetingTranscriptSegmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<MeetingTranscriptSegmentDto> TranscribeAudio(
        long meetingId,
        [FromForm] MeetingAudioClipForm input,
        CancellationToken ct = default)
    {
        await using var stream = input.File.OpenReadStream();
        return await service.TranscribeAudioClipAsync(
            meetingId,
            stream,
            input.File.FileName,
            input.File.ContentType,
            input.Language,
            ct);
    }

    [HttpPatch("transcript/{segmentId:long}/highlight")]
    [ProducesResponseType<MeetingTranscriptSegmentDto>(StatusCodes.Status200OK)]
    public Task<MeetingTranscriptSegmentDto> ToggleHighlight(
        long meetingId,
        long segmentId,
        [FromBody] ToggleTranscriptHighlightRequest request,
        CancellationToken ct = default) =>
        service.ToggleHighlightAsync(meetingId, segmentId, request.IsHighlighted, ct);

    [HttpDelete("transcript/{segmentId:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSegment(
        long meetingId,
        long segmentId,
        CancellationToken ct = default)
    {
        await service.DeleteSegmentAsync(meetingId, segmentId, ct);
        return NoContent();
    }

    [HttpGet("insights")]
    [ProducesResponseType<IReadOnlyList<MeetingAiInsightDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<MeetingAiInsightDto>> Insights(
        long meetingId,
        [FromQuery] int limit = 10,
        CancellationToken ct = default) =>
        service.GetInsightsAsync(meetingId, limit, ct);

    [HttpPost("summarize")]
    [ProducesResponseType<MeetingAiInsightDto>(StatusCodes.Status200OK)]
    public Task<MeetingAiInsightDto> Summarize(
        long meetingId,
        [FromBody] GenerateMeetingAiSummaryRequest request,
        CancellationToken ct = default) =>
        service.GenerateSummaryAsync(meetingId, request, ct);

    [HttpPost("ask")]
    [ProducesResponseType<MeetingAiAnswerDto>(StatusCodes.Status200OK)]
    public Task<MeetingAiAnswerDto> Ask(
        long meetingId,
        [FromBody] AskMeetingAiRequest request,
        CancellationToken ct = default) =>
        service.AskAsync(meetingId, request, ct);

    [HttpPost("coach")]
    [ProducesResponseType<MeetingAiCoachDto>(StatusCodes.Status200OK)]
    public Task<MeetingAiCoachDto> Coach(
        long meetingId,
        [FromBody] MeetingAiCoachRequest request,
        CancellationToken ct = default) =>
        service.CoachAsync(meetingId, request, ct);
}

public sealed class MeetingAudioClipForm
{
    [Required]
    public IFormFile File { get; init; } = null!;

    public string? Language { get; init; }
}
