using System.Collections.Generic;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record ProjectEvidencePayload(
    long ProjectId,
    string ProjectStatus,
    int TotalTasks,
    int RetrievedTasks,
    bool TasksTruncated,
    IReadOnlyList<MilestoneEvidenceItem> Milestones,
    IReadOnlyList<TaskEvidenceItem> Tasks,
    IReadOnlyList<ProgressReportEvidenceItem> ProgressReports,
    IReadOnlyList<MeetingEvidenceItem> Meetings,
    ContributionEvidenceItem? ContributionSummary);

public sealed record MilestoneEvidenceItem(
    string Id,
    long MilestoneId,
    string Title,
    string Status,
    string? DueDate,
    int SortOrder,
    string? Description);

public sealed record TaskEvidenceItem(
    string Id,
    long TaskId,
    string Title,
    string Status,
    string? Priority,
    string? DueAt,
    bool IsOverdue,
    bool IsBlocked,
    string? Description);

public sealed record ProgressReportEvidenceItem(
    string Id,
    long ReportId,
    string ReportType,
    string Status,
    string Period,
    string? Summary,
    string? CompletedWork,
    string? IssuesAndRisks);

public sealed record MeetingEvidenceItem(
    string Id,
    long MeetingId,
    string Title,
    string Status,
    string StartAt,
    string? Agenda,
    string? MeetingNotes);

public sealed record ContributionEvidenceItem(
    string Id,
    int MemberCount,
    int TotalTasksCompleted,
    string? SnapshotAt);
