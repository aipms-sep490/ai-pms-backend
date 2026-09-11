using System;
using System.Linq;
using AIPMS.Application.Features.ProgressReports.DTOs;
using ReportEntity = AIPMS.Infrastructure.Persistence.Generated.Models.ProgressReport;
using FeedbackEntity = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorFeedback;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class ProgressReportMapper
{
    public static ProgressReportDto ToDto(this ReportEntity report)
    {
        var isLate = report.SubmittedAt.HasValue &&
            report.SubmittedAt.Value.Date > report.PeriodEnd.ToDateTime(TimeOnly.MinValue).Date;

        return new ProgressReportDto(
            report.Id,
            report.ProjectId,
            report.SubmittedBy,
            report.SubmittedByNavigation?.FullName ?? string.Empty,
            report.ReportType,
            report.PeriodStart,
            report.PeriodEnd,
            report.Summary,
            report.CompletedWork,
            report.PlannedWork,
            report.IssuesAndRisks,
            report.Status,
            report.SubmittedAt,
            isLate,
            report.CreatedAt,
            report.UpdatedAt);
    }

    public static ProgressReportDetailDto ToDetailDto(this ReportEntity report)
    {
        var isLate = report.SubmittedAt.HasValue &&
            report.SubmittedAt.Value.Date > report.PeriodEnd.ToDateTime(TimeOnly.MinValue).Date;

        var feedbacks = report.SupervisorFeedbacks
            .OrderBy(f => f.CreatedAt)
            .Select(f => f.ToDto())
            .ToList();

        return new ProgressReportDetailDto(
            report.Id,
            report.ProjectId,
            report.SubmittedBy,
            report.SubmittedByNavigation?.FullName ?? string.Empty,
            report.ReportType,
            report.PeriodStart,
            report.PeriodEnd,
            report.Summary,
            report.CompletedWork,
            report.PlannedWork,
            report.IssuesAndRisks,
            report.Status,
            report.SubmittedAt,
            isLate,
            report.CreatedAt,
            report.UpdatedAt,
            feedbacks);
    }

    public static ProgressReportFeedbackDto ToDto(this FeedbackEntity feedback) =>
        new(
            feedback.Id,
            feedback.ProjectId,
            feedback.SupervisorAssignmentId,
            feedback.SupervisorAssignment?.SupervisorProfile?.UserId ?? 0,
            feedback.SupervisorAssignment?.SupervisorProfile?.User?.FullName ?? string.Empty,
            feedback.ProgressReportId,
            feedback.FeedbackText,
            feedback.CreatedAt,
            feedback.UpdatedAt);
}
