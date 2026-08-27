using AIPMS.Application.Features.ProgressMeetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Meeting = AIPMS.Infrastructure.Persistence.Generated.Models.Meeting;
using ProgressReport = AIPMS.Infrastructure.Persistence.Generated.Models.ProgressReport;

namespace AIPMS.Infrastructure.Persistence.Mappers;

internal static class ProgressMeetingMapper
{
    internal static IQueryable<ProgressReportDto> ProjectReports(
        this IQueryable<ProgressReport> source,
        AipmsDbContext db) => source.AsNoTracking().Select(x => new ProgressReportDto(
            x.Id, x.ProjectId, x.SubmittedBy, x.ReportType, x.PeriodStart, x.PeriodEnd,
            x.Summary, x.CompletedWork, x.PlannedWork, x.IssuesAndRisks, x.Status, x.SubmittedAt,
            x.CreatedAt, x.UpdatedAt,
            db.Set<ProgressReportMetadata>().Where(m => m.ReportId == x.Id).Select(m => (long?)m.ReportPeriodId).FirstOrDefault(),
            (from m in db.Set<ProgressReportMetadata>() join p in db.Set<ProgressReportPeriod>() on m.ReportPeriodId equals p.Id where m.ReportId == x.Id select (DateTime?)p.DeadlineAt).FirstOrDefault(),
            (from m in db.Set<ProgressReportMetadata>() join p in db.Set<ProgressReportPeriod>() on m.ReportPeriodId equals p.Id where m.ReportId == x.Id select p.LatePolicy).FirstOrDefault(),
            db.Set<ProgressReportMetadata>().Where(m => m.ReportId == x.Id).Select(m => m.IsLate).FirstOrDefault(),
            db.Set<ProgressReportSection>().Where(s => s.ReportId == x.Id).OrderBy(s => s.Id).Select(s => new ReportSectionDto(s.SectionType, s.Content)).ToList(),
            db.Set<ProgressReportContribution>().Where(c => c.ReportId == x.Id).OrderBy(c => c.CreatedAt).Select(c => new ReportContributionDto(c.Id, c.ContributorId, c.SectionType, c.Content, c.CreatedAt)).ToList(),
            x.SupervisorFeedbacks.OrderBy(f => f.CreatedAt).Select(f => new FeedbackDto(f.Id, f.SupervisorAssignmentId, f.FeedbackText, f.CreatedAt)).ToList()));

    internal static IQueryable<MeetingDto> ProjectMeetings(
        this IQueryable<Meeting> source,
        AipmsDbContext db) => source.AsNoTracking().Select(x => new MeetingDto(
            x.Id, x.ProjectId, x.Title, x.Agenda, x.MeetingNotes, x.StartAt, x.EndAt,
            x.Location, x.OnlineUrl, x.Status, x.CreatedBy, x.CreatedAt, x.UpdatedAt,
            x.MeetingParticipants.OrderBy(p => p.Id).Select(p => new MeetingParticipantDto(p.UserId, p.User.FullName, p.AttendanceStatus)).ToList(),
            db.Set<MeetingDecision>().Where(d => d.MeetingId == x.Id).OrderBy(d => d.Id).Select(d => new MeetingTextItemDto(d.Id, d.Content, d.CreatedBy, d.CreatedAt)).ToList(),
            db.Set<MeetingBlocker>().Where(b => b.MeetingId == x.Id).OrderBy(b => b.Id).Select(b => new MeetingTextItemDto(b.Id, b.Content, b.CreatedBy, b.CreatedAt)).ToList(),
            db.Set<MeetingActionItem>().Where(a => a.MeetingId == x.Id).OrderBy(a => a.Id).Select(a => new MeetingActionItemDto(a.Id, a.Title, a.Description, a.OwnerUserId, a.DueDate, a.Status, a.TaskId, a.MilestoneId, a.CreatedBy, a.CreatedAt)).ToList(),
            x.SupervisorFeedbacks.OrderBy(f => f.CreatedAt).Select(f => new FeedbackDto(f.Id, f.SupervisorAssignmentId, f.FeedbackText, f.CreatedAt)).ToList()));
}
