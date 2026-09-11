using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Meetings.DTOs;

public sealed record MeetingDto(
    long Id,
    long ProjectId,
    string Title,
    string? Agenda,
    string? MeetingNotes,
    DateTime StartAt,
    DateTime? EndAt,
    string? Location,
    string? OnlineUrl,
    string Status,
    long CreatedBy,
    string CreatedByName,
    int ParticipantCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MeetingDetailDto(
    long Id,
    long ProjectId,
    string Title,
    string? Agenda,
    string? MeetingNotes,
    DateTime StartAt,
    DateTime? EndAt,
    string? Location,
    string? OnlineUrl,
    string Status,
    long CreatedBy,
    string CreatedByName,
    IReadOnlyList<MeetingParticipantDto> Participants,
    IReadOnlyList<MeetingFeedbackDto> Feedbacks,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MeetingParticipantDto(
    long Id,
    long MeetingId,
    long UserId,
    string FullName,
    string Email,
    string? AttendanceStatus,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record MeetingFeedbackDto(
    long Id,
    long ProjectId,
    long SupervisorAssignmentId,
    long SupervisorUserId,
    string SupervisorName,
    long? MeetingId,
    string FeedbackText,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateMeetingRequest(
    string Title,
    string? Agenda,
    DateTime StartAt,
    DateTime? EndAt,
    string? Location,
    string? OnlineUrl,
    IReadOnlyList<long>? ParticipantUserIds);

public sealed record UpdateMeetingRequest(
    string Title,
    string? Agenda,
    DateTime StartAt,
    DateTime? EndAt,
    string? Location,
    string? OnlineUrl);

public sealed record UpdateMeetingNotesRequest(
    string? MeetingNotes,
    string? Status,
    IReadOnlyList<ParticipantAttendanceUpdate>? Attendances);

public sealed record ParticipantAttendanceUpdate(
    long UserId,
    string AttendanceStatus);

public sealed record AddMeetingParticipantRequest(
    long UserId,
    string? AttendanceStatus);

public sealed record AddMeetingFeedbackRequest(
    string FeedbackText);
