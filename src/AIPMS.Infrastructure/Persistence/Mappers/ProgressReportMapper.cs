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
            report.IsLate,
            report.CreatedAt,
            report.UpdatedAt,
            report.ConcurrencyToken.ToString("N"),
            report.ProgressReportPeriodId,
            report.InProgressWork,
            report.Blockers,
            report.Risks,
            report.NextActions);
    }

    public static ProgressReportDetailDto ToDetailDto(this ReportEntity report)
    {
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
            report.IsLate,
            report.CreatedAt,
            report.UpdatedAt,
            feedbacks,
            report.ConcurrencyToken.ToString("N"),
            report.ProgressReportPeriodId,
            report.InProgressWork,
            report.Blockers,
            report.Risks,
            report.NextActions);
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
