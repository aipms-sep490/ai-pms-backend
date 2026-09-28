using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.DTOs;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IMeetingGovernanceService
{
    Task<PagedResult<MeetingDecisionDto>> Decisions(long meetingId, int page, int pageSize, CancellationToken ct);
    Task<PagedResult<MeetingActionItemDto>> Actions(long meetingId, int page, int pageSize, CancellationToken ct);
    Task<MeetingDecisionDto> Decide(long meetingId, CreateMeetingDecisionRequest input, CancellationToken ct);
    Task<MeetingActionItemDto> SaveAction(long meetingId, long? actionId, SaveMeetingActionItemRequest input, CancellationToken ct);
}
