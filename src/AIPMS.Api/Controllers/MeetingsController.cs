using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Commands;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using AIPMS.Application.Features.ProgressMeetings.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.Api.Controllers;
[ApiController, Authorize, Route("api/v1")]
public sealed class MeetingsController(ISender sender) : ControllerBase
{
    [HttpPost("projects/{projectId:long}/meetings")] public async Task<ActionResult<MeetingDto>> Create(long projectId, CreateMeetingPayload p, CancellationToken ct) => Ok(await sender.Send(new CreateMeetingCommand(projectId, p.Title, p.Agenda, p.StartAt, p.EndAt, p.Location, p.OnlineUrl, p.ParticipantIds), ct));
    [HttpGet("projects/{projectId:long}/meetings")] public async Task<ActionResult<PagedResult<MeetingDto>>> List(long projectId, [FromQuery] string? status, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => Ok(await sender.Send(new ListMeetingsQuery(projectId, status, from, to, pageNumber, pageSize), ct));
    [HttpGet("meetings/{id:long}")] public async Task<ActionResult<MeetingDto>> Get(long id, CancellationToken ct) => Ok(await sender.Send(new GetMeetingQuery(id), ct));
    [HttpPut("meetings/{id:long}")] public async Task<IActionResult> Update(long id, UpdateMeetingPayload p, CancellationToken ct) { await sender.Send(new UpdateMeetingCommand(id, p.Title, p.Agenda, p.StartAt, p.EndAt, p.Location, p.OnlineUrl), ct); return NoContent(); }
    [HttpDelete("meetings/{id:long}")] public async Task<IActionResult> Cancel(long id, CancellationToken ct) { await sender.Send(new CancelMeetingCommand(id), ct); return NoContent(); }
    [HttpPut("meetings/{id:long}/minutes")] public async Task<IActionResult> Minutes(long id, MeetingMinutesPayload p, CancellationToken ct) { await sender.Send(new UpdateMeetingMinutesCommand(id, p.MeetingNotes, p.Complete), ct); return NoContent(); }
    [HttpPut("meetings/{id:long}/participants/{userId:long}/attendance")] public async Task<IActionResult> Attendance(long id, long userId, AttendancePayload p, CancellationToken ct) { await sender.Send(new SetMeetingAttendanceCommand(id, userId, p.Status), ct); return NoContent(); }
    [HttpPut("meetings/{id:long}/participants")] public async Task<IActionResult> Participants(long id, ParticipantsPayload p, CancellationToken ct) { await sender.Send(new ReplaceMeetingParticipantsCommand(id, p.ParticipantIds), ct); return NoContent(); }
    [HttpPost("meetings/{id:long}/feedback")] public async Task<IActionResult> Feedback(long id, FeedbackPayload p, CancellationToken ct) { await sender.Send(new AddMeetingFeedbackCommand(id, p.FeedbackText), ct); return NoContent(); }
    [HttpPost("meetings/{id:long}/decisions")] public async Task<IActionResult> Decision(long id, MeetingTextPayload p, CancellationToken ct) { await sender.Send(new AddMeetingDecisionCommand(id, p.Content), ct); return NoContent(); }
    [HttpPost("meetings/{id:long}/blockers")] public async Task<IActionResult> Blocker(long id, MeetingTextPayload p, CancellationToken ct) { await sender.Send(new AddMeetingBlockerCommand(id, p.Content), ct); return NoContent(); }
    [HttpPost("meetings/{id:long}/action-items")] public async Task<IActionResult> ActionItem(long id, CreateActionItemPayload p, CancellationToken ct) { await sender.Send(new CreateMeetingActionItemCommand(id, p.Title, p.Description, p.OwnerUserId, p.DueDate, p.Status, p.TaskId, p.MilestoneId), ct); return NoContent(); }
    [HttpPut("meetings/{id:long}/action-items/{actionItemId:long}/status")] public async Task<IActionResult> ActionItemStatus(long id, long actionItemId, ActionItemStatusPayload p, CancellationToken ct) { await sender.Send(new UpdateMeetingActionItemStatusCommand(id, actionItemId, p.Status), ct); return NoContent(); }
}
public sealed record CreateMeetingPayload(string Title, string? Agenda, DateTime StartAt, DateTime? EndAt, string? Location, string? OnlineUrl, IReadOnlyCollection<long> ParticipantIds);
public sealed record UpdateMeetingPayload(string Title, string? Agenda, DateTime StartAt, DateTime? EndAt, string? Location, string? OnlineUrl);
public sealed record MeetingMinutesPayload(string? MeetingNotes, bool Complete);
public sealed record AttendancePayload(string Status);
public sealed record ParticipantsPayload(IReadOnlyCollection<long> ParticipantIds);
public sealed record MeetingTextPayload(string Content);
public sealed record CreateActionItemPayload(string Title, string? Description, long OwnerUserId, DateOnly? DueDate, string Status, long? TaskId, long? MilestoneId);
public sealed record ActionItemStatusPayload(string Status);
