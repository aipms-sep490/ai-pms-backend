using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.DTOs;

namespace AIPMS.Application.Features.ProgressMeetings.Abstractions;

public interface IProgressMeetingRepository
{
    Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct);
    Task<bool> IsProjectMemberAsync(long userId, long projectId, CancellationToken ct);
    Task<bool> IsProjectLeaderAsync(long userId, long projectId, CancellationToken ct);
    Task<long?> GetActiveSupervisorAssignmentAsync(long userId, long projectId, CancellationToken ct);
    Task<bool> AreValidParticipantsAsync(long projectId, IReadOnlyCollection<long> userIds, CancellationToken ct);
    Task<ReportPeriodDto?> GetReportPeriodAsync(long id, CancellationToken ct);
    Task<ProgressReportDto?> GetReportAsync(long id, CancellationToken ct);
    Task<PagedResult<ProgressReportDto>> ListReportsAsync(long projectId, ReportListFilter filter, CancellationToken ct);
    Task<long> CreateReportAsync(CreateReportData data, CancellationToken ct);
    Task<bool> UpdateDraftReportAsync(long id, UpdateReportData data, CancellationToken ct);
    Task<bool> SubmitDraftReportAsync(long id, DateTime submittedAt, CancellationToken ct);
    Task AddReportFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct);
    Task AddContributionAsync(long reportId, long contributorId, string sectionType, string content, CancellationToken ct);
    Task<MeetingDto?> GetMeetingAsync(long id, CancellationToken ct);
    Task<PagedResult<MeetingDto>> ListMeetingsAsync(long projectId, MeetingListFilter filter, CancellationToken ct);
    Task<long> CreateMeetingAsync(CreateMeetingData data, CancellationToken ct);
    Task<bool> UpdateScheduledMeetingAsync(long id, UpdateMeetingData data, CancellationToken ct);
    Task<bool> CancelMeetingAsync(long id, CancellationToken ct);
    Task<bool> UpdateMinutesAsync(long id, string? notes, bool complete, CancellationToken ct);
    Task<bool> SetAttendanceAsync(long meetingId, long userId, string status, CancellationToken ct);
    Task ReplaceParticipantsAsync(long meetingId, long actorId, string title, IReadOnlyCollection<long> userIds, CancellationToken ct);
    Task AddDecisionAsync(long meetingId, long actorId, string content, CancellationToken ct);
    Task AddBlockerAsync(long meetingId, long actorId, string content, CancellationToken ct);
    Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken ct);
    Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken ct);
    Task AddActionItemAsync(CreateActionItemData data, CancellationToken ct);
    Task<bool> UpdateActionItemStatusAsync(long meetingId, long actionItemId, string status, CancellationToken ct);
    Task AddMeetingFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct);
}
