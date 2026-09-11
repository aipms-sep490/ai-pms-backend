using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.DTOs;

namespace AIPMS.Application.Features.Meetings.Abstractions;

public interface IMeetingRepository
{
    Task<MeetingDto?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<MeetingDetailDto?> GetDetailByIdAsync(long id, CancellationToken cancellationToken);

    Task<PagedResult<MeetingDto>> GetMeetingsAsync(
        long projectId,
        string? status,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<MeetingDto> CreateAsync(
        long projectId,
        long createdBy,
        string title,
        string? agenda,
        DateTime startAt,
        DateTime? endAt,
        string? location,
        string? onlineUrl,
        IReadOnlyList<long>? participantUserIds,
        DateTime now,
        CancellationToken cancellationToken);

    Task<MeetingDto> UpdateAsync(
        long id,
        string title,
        string? agenda,
        DateTime startAt,
        DateTime? endAt,
        string? location,
        string? onlineUrl,
        DateTime now,
        CancellationToken cancellationToken);

    Task<MeetingDto> CancelAsync(long id, DateTime now, CancellationToken cancellationToken);

    Task<MeetingDto> UpdateNotesAsync(
        long id,
        string? meetingNotes,
        string? status,
        IReadOnlyList<ParticipantAttendanceUpdate>? attendances,
        DateTime now,
        CancellationToken cancellationToken);

    Task<MeetingParticipantDto> AddParticipantAsync(
        long meetingId,
        long userId,
        string? attendanceStatus,
        DateTime now,
        CancellationToken cancellationToken);

    Task RemoveParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken);

    Task<MeetingFeedbackDto> AddFeedbackAsync(
        long meetingId,
        long supervisorAssignmentId,
        string feedbackText,
        DateTime now,
        CancellationToken cancellationToken);

    Task<long?> GetProjectIdAsync(long meetingId, CancellationToken cancellationToken);

    Task<string?> GetStatusAsync(long meetingId, CancellationToken cancellationToken);

    Task<bool> ProjectExistsAsync(long projectId, CancellationToken cancellationToken);

    Task<bool> IsProjectMemberOrSupervisorAsync(long projectId, long userId, CancellationToken cancellationToken);

    Task<bool> CanManageMeetingAsync(long meetingId, long userId, CancellationToken cancellationToken);

    Task<bool> CanScheduleMeetingAsync(long projectId, long userId, CancellationToken cancellationToken);

    Task<bool> IsParticipantAsync(long meetingId, long userId, CancellationToken cancellationToken);

    Task<long?> GetActiveSupervisorAssignmentIdAsync(long projectId, long supervisorUserId, CancellationToken cancellationToken);
}
