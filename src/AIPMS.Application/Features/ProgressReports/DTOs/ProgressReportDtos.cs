using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.ProgressReports.DTOs;

public sealed record ProgressReportDto(
    long Id,
    long ProjectId,
    long SubmittedBy,
    string SubmittedByName,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    string Status,
    DateTime? SubmittedAt,
    bool? IsLate,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ConcurrencyToken = null,
    long? ProgressReportPeriodId = null,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record ProgressReportDetailDto(
    long Id,
    long ProjectId,
    long SubmittedBy,
    string SubmittedByName,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    string Status,
    DateTime? SubmittedAt,
    bool? IsLate,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ProgressReportFeedbackDto> Feedbacks,
    string? ConcurrencyToken = null,
    long? ProgressReportPeriodId = null,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record ProgressReportFeedbackDto(
    long Id,
    long ProjectId,
    long SupervisorAssignmentId,
    long SupervisorUserId,
    string SupervisorName,
    long? ProgressReportId,
    string FeedbackText,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateProgressReportRequest(
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    long? ProgressReportPeriodId = null,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record UpdateProgressReportRequest(
    string Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    string? ConcurrencyToken = null,
    long? ProgressReportPeriodId = null,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record AddProgressReportFeedbackRequest(
    string FeedbackText);
