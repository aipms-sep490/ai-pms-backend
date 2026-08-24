using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.ProgressMeetings.DTOs;

public sealed record ProgressReportDto(long Id, long ProjectId, long SubmittedBy, string ReportType,
    DateOnly PeriodStart, DateOnly PeriodEnd, string Summary, string? CompletedWork,
    string? PlannedWork, string? IssuesAndRisks, string Status, DateTime? SubmittedAt,
    DateTime CreatedAt, DateTime UpdatedAt, long? ReportPeriodId, DateTime? DeadlineAt, string? LatePolicy,
    bool IsLate, IReadOnlyList<ReportSectionDto> Sections, IReadOnlyList<ReportContributionDto> Contributions,
    IReadOnlyList<FeedbackDto> Feedback);
public sealed record ReportSectionDto(string SectionType, string Content);
public sealed record ReportContributionDto(long Id, long ContributorId, string SectionType, string Content, DateTime CreatedAt);

public sealed record MeetingParticipantDto(long UserId, string? FullName, string? AttendanceStatus);
public sealed record MeetingDto(long Id, long ProjectId, string Title, string? Agenda,
    string? MeetingNotes, DateTime StartAt, DateTime? EndAt, string? Location, string? OnlineUrl,
    string Status, long CreatedBy, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<MeetingParticipantDto> Participants, IReadOnlyList<MeetingTextItemDto> Decisions,
    IReadOnlyList<MeetingTextItemDto> Blockers, IReadOnlyList<MeetingActionItemDto> ActionItems,
    IReadOnlyList<FeedbackDto> Feedback);
public sealed record MeetingTextItemDto(long Id, string Content, long CreatedBy, DateTime CreatedAt);
public sealed record MeetingActionItemDto(long Id, string Title, string? Description, long OwnerUserId,
    DateOnly? DueDate, string Status, long? TaskId, long? MilestoneId, long CreatedBy, DateTime CreatedAt);
public sealed record FeedbackDto(long Id, long SupervisorAssignmentId, string FeedbackText, DateTime CreatedAt);

public sealed record ReportListFilter(string? ReportType, string? Status, DateOnly? From, DateOnly? To,
    int PageNumber = 1, int PageSize = 20);
public sealed record MeetingListFilter(string? Status, DateTime? From, DateTime? To,
    int PageNumber = 1, int PageSize = 20);

public sealed record CreateReportData(long ProjectId, long ActorId, long ReportPeriodId, string Summary,
    IReadOnlyDictionary<string, string> Sections);
public sealed record UpdateReportData(string Summary, IReadOnlyDictionary<string, string> Sections);
public sealed record ReportPeriodDto(long Id, long ProjectId, string ReportType, DateOnly PeriodStart,
    DateOnly PeriodEnd, DateTime DeadlineAt, string LatePolicy, string Status);
public sealed record CreateMeetingData(long ProjectId, long ActorId, string Title, string? Agenda,
    DateTime StartAt, DateTime? EndAt, string? Location, string? OnlineUrl, IReadOnlyCollection<long> ParticipantIds);
public sealed record UpdateMeetingData(string Title, string? Agenda, DateTime StartAt, DateTime? EndAt,
    string? Location, string? OnlineUrl);
public sealed record CreateActionItemData(long MeetingId, long ActorId, string Title, string? Description,
    long OwnerUserId, DateOnly? DueDate, string Status, long? TaskId, long? MilestoneId);
