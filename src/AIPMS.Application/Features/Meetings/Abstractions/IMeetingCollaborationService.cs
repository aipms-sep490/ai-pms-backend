using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.DTOs;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IMeetingCollaborationService
{
    Task<MeetingSeriesDto> CreateSeries(long projectId, CreateMeetingSeriesRequest input, CancellationToken ct);
    Task<MeetingCollaborationDto> Get(long meetingId, CancellationToken ct);
    Task<MeetingRsvpDto> Respond(long meetingId, RespondMeetingRequest input, CancellationToken ct);
    Task<MeetingCollaborationDto> LockNotes(long meetingId, LockMeetingNotesRequest input, CancellationToken ct);
    Task<PagedResult<MeetingNotesVersionDto>> NotesHistory(long meetingId, int page, int pageSize, CancellationToken ct);
}
