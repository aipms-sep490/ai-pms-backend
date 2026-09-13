using System;
using System.Linq;
using AIPMS.Application.Features.Meetings.DTOs;
using MeetingEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Meeting;
using ParticipantEntity = AIPMS.Infrastructure.Persistence.Generated.Models.MeetingParticipant;
using FeedbackEntity = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorFeedback;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class MeetingMapper
{
    public static MeetingDto ToDto(this MeetingEntity meeting) =>
        new(
            meeting.Id,
            meeting.ProjectId,
            meeting.Title,
            meeting.Agenda,
            meeting.MeetingNotes,
            meeting.StartAt,
            meeting.EndAt,
            meeting.Location,
            meeting.OnlineUrl,
            meeting.Status,
            meeting.CreatedBy,
            meeting.CreatedByNavigation?.FullName ?? string.Empty,
            meeting.MeetingParticipants.Count,
            meeting.CreatedAt,
            meeting.UpdatedAt);

    public static MeetingDetailDto ToDetailDto(this MeetingEntity meeting)
    {
        var participants = meeting.MeetingParticipants
            .OrderBy(p => p.Id)
            .Select(p => p.ToDto())
            .ToList();

        var feedbacks = meeting.SupervisorFeedbacks
            .OrderBy(f => f.CreatedAt)
            .Select(f => f.ToMeetingFeedbackDto())
            .ToList();

        return new MeetingDetailDto(
            meeting.Id,
            meeting.ProjectId,
            meeting.Title,
            meeting.Agenda,
            meeting.MeetingNotes,
            meeting.StartAt,
            meeting.EndAt,
            meeting.Location,
            meeting.OnlineUrl,
            meeting.Status,
            meeting.CreatedBy,
            meeting.CreatedByNavigation?.FullName ?? string.Empty,
            participants,
            feedbacks,
            meeting.CreatedAt,
            meeting.UpdatedAt);
    }

    public static MeetingParticipantDto ToDto(this ParticipantEntity participant) =>
        new(
            participant.Id,
            participant.MeetingId,
            participant.UserId,
            participant.User?.FullName ?? string.Empty,
            participant.User?.Email ?? string.Empty,
            participant.AttendanceStatus,
            participant.CreatedAt,
            participant.UpdatedAt);

    public static MeetingFeedbackDto ToMeetingFeedbackDto(this FeedbackEntity feedback) =>
        new(
            feedback.Id,
            feedback.ProjectId,
            feedback.SupervisorAssignmentId,
            feedback.SupervisorAssignment?.SupervisorProfile?.UserId ?? 0,
            feedback.SupervisorAssignment?.SupervisorProfile?.User?.FullName ?? string.Empty,
            feedback.MeetingId,
            feedback.FeedbackText,
            feedback.CreatedAt,
            feedback.UpdatedAt);
}
