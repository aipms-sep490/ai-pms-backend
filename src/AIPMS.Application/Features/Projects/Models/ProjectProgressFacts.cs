using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Projects.Models;

public sealed record ProjectProgressFacts(
    long ProjectId,
    string ProjectStatus,
    long TeamId,
    int TeamMemberCount,
    IReadOnlyList<MilestoneFact> Milestones,
    IReadOnlyList<TaskFact> Tasks,
    IReadOnlyList<ProgressReportFact> ProgressReports,
    IReadOnlyList<MeetingFact> Meetings);

public sealed record MilestoneFact(
    long Id,
    string Title,
    string Status,
    DateOnly? StartDate,
    DateOnly? DueDate,
    int SortOrder);

public sealed record TaskFact(
    long Id,
    long MilestoneId,
    string Title,
    string Status,
    string? Priority,
    DateTime? StartAt,
    DateTime? DueAt,
    DateTime? CompletedAt,
    int AssigneeCount);

public sealed record ProgressReportFact(
    long Id,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    DateTime? SubmittedAt);

public sealed record MeetingFact(
    long Id,
    string Title,
    string Status,
    DateTime StartAt,
    DateTime? EndAt);
