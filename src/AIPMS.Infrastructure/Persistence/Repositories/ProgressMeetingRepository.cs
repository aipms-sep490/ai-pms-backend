using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Meeting = AIPMS.Infrastructure.Persistence.Generated.Models.Meeting;
using MeetingParticipant = AIPMS.Infrastructure.Persistence.Generated.Models.MeetingParticipant;
using Notification = AIPMS.Infrastructure.Persistence.Generated.Models.Notification;
using NotificationRecipient = AIPMS.Infrastructure.Persistence.Generated.Models.NotificationRecipient;
using ProgressReport = AIPMS.Infrastructure.Persistence.Generated.Models.ProgressReport;
using SupervisorFeedback = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorFeedback;
using ExtensionModels = AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class ProgressMeetingRepository(AipmsDbContext db) : IProgressMeetingRepository
{
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) => db.Projects.AnyAsync(x => x.Id == projectId, ct);

    public Task<bool> IsProjectMemberAsync(long userId, long projectId, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.Team.TeamMembers.Any(m => m.UserId == userId && m.LeftAt == null), ct);

    public Task<bool> IsProjectLeaderAsync(long userId, long projectId, CancellationToken ct) =>
        db.Projects.AnyAsync(p => p.Id == projectId && p.Team.TeamMembers.Any(m => m.UserId == userId && m.IsLeader && m.LeftAt == null), ct);

    public Task<long?> GetActiveSupervisorAssignmentAsync(long userId, long projectId, CancellationToken ct) =>
        db.SupervisorAssignments.Where(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == userId)
            .Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);

    public async Task<bool> AreValidParticipantsAsync(long projectId, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return true;
        var memberIds = db.TeamMembers.Where(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.LeftAt == null).Select(m => m.UserId);
        var supervisorIds = db.SupervisorAssignments.Where(a => a.ProjectId == projectId && a.EndedAt == null).Select(a => a.SupervisorProfile.UserId);
        var valid = await memberIds.Concat(supervisorIds).Distinct().CountAsync(x => userIds.Contains(x), ct);
        return valid == userIds.Distinct().Count();
    }
    public Task<ReportPeriodDto?> GetReportPeriodAsync(long id, CancellationToken ct) => db.Set<ExtensionModels.ProgressReportPeriod>().AsNoTracking()
        .Where(x => x.Id == id).Select(x => new ReportPeriodDto(x.Id, x.ProjectId, x.ReportType, x.PeriodStart, x.PeriodEnd, x.DeadlineAt, x.LatePolicy, x.Status)).SingleOrDefaultAsync(ct);

    public async Task<ProgressReportDto?> GetReportAsync(long id, CancellationToken ct) =>
        await ReportQuery(db.ProgressReports.Where(x => x.Id == id)).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<ProgressReportDto>> ListReportsAsync(long projectId, ReportListFilter f, CancellationToken ct)
    {
        var q = db.ProgressReports.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(f.ReportType)) q = q.Where(x => x.ReportType == f.ReportType);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(x => x.Status == f.Status);
        if (f.From.HasValue) q = q.Where(x => x.PeriodStart >= f.From.Value);
        if (f.To.HasValue) q = q.Where(x => x.PeriodEnd <= f.To.Value);
        var count = await q.CountAsync(ct);
        var items = await ReportQuery(q.OrderByDescending(x => x.PeriodStart).Skip((f.PageNumber - 1) * f.PageSize).Take(f.PageSize)).ToListAsync(ct);
        return new(items, f.PageNumber, f.PageSize, count, (int)Math.Ceiling(count / (double)f.PageSize));
    }

    public async Task<long> CreateReportAsync(CreateReportData d, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var period = await db.Set<ExtensionModels.ProgressReportPeriod>().SingleOrDefaultAsync(x => x.Id == d.ReportPeriodId && x.ProjectId == d.ProjectId && x.Status == "OPEN", ct)
            ?? throw new ConflictException("The configured report period is not open for this project.");
        if (await db.Set<ExtensionModels.ProgressReportMetadata>().AnyAsync(x => x.ReportPeriodId == period.Id, ct)) throw new ConflictException("A report already exists for this configured period.");
        var entity = new ProgressReport { ProjectId = d.ProjectId, SubmittedBy = d.ActorId, ReportType = period.ReportType,
            PeriodStart = period.PeriodStart, PeriodEnd = period.PeriodEnd, Summary = d.Summary,
            CompletedWork = d.Sections["COMPLETED"], PlannedWork = d.Sections["NEXT_ACTIONS"],
            IssuesAndRisks = $"Blockers: {d.Sections["BLOCKERS"]}\nRisks: {d.Sections["RISKS"]}", Status = "DRAFT" };
        db.ProgressReports.Add(entity); await db.SaveChangesAsync(ct);
        db.Set<ExtensionModels.ProgressReportMetadata>().Add(new() { ReportId = entity.Id, ReportPeriodId = period.Id });
        db.Set<ExtensionModels.ProgressReportSection>().AddRange(d.Sections.Select(x => new ExtensionModels.ProgressReportSection { ReportId = entity.Id, SectionType = x.Key, Content = x.Value }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return entity.Id;
    }

    public async Task<bool> UpdateDraftReportAsync(long id, UpdateReportData d, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.ProgressReports.Where(x => x.Id == id && x.Status == "DRAFT")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Summary, d.Summary).SetProperty(x => x.CompletedWork, d.Sections["COMPLETED"])
                .SetProperty(x => x.PlannedWork, d.Sections["NEXT_ACTIONS"]).SetProperty(x => x.IssuesAndRisks, $"Blockers: {d.Sections["BLOCKERS"]}\nRisks: {d.Sections["RISKS"]}")
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        if (updated == 1)
            foreach (var section in d.Sections) await db.Set<ExtensionModels.ProgressReportSection>().Where(x => x.ReportId == id && x.SectionType == section.Key)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Content, section.Value).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        await tx.CommitAsync(ct);
        return updated == 1;
    }

    public async Task<bool> SubmitDraftReportAsync(long id, DateTime submittedAt, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var updated = await db.ProgressReports.Where(x => x.Id == id && x.Status == "DRAFT")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUBMITTED").SetProperty(x => x.SubmittedAt, submittedAt).SetProperty(x => x.UpdatedAt, submittedAt), ct);
        if (updated == 1)
        {
            var deadline = await (from m in db.Set<ExtensionModels.ProgressReportMetadata>() join p in db.Set<ExtensionModels.ProgressReportPeriod>() on m.ReportPeriodId equals p.Id where m.ReportId == id select p.DeadlineAt).SingleAsync(ct);
            await db.Set<ExtensionModels.ProgressReportMetadata>().Where(x => x.ReportId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsLate, submittedAt > deadline).SetProperty(x => x.UpdatedAt, submittedAt), ct);
        }
        await tx.CommitAsync(ct); return updated == 1;
    }
    public async Task AddContributionAsync(long reportId, long contributorId, string sectionType, string content, CancellationToken ct)
    { db.Set<ExtensionModels.ProgressReportContribution>().Add(new() { ReportId = reportId, ContributorId = contributorId, SectionType = sectionType, Content = content }); await db.SaveChangesAsync(ct); }

    public async Task AddReportFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.SupervisorFeedbacks.Add(new SupervisorFeedback { ProjectId = projectId, SupervisorAssignmentId = assignmentId, ProgressReportId = id, FeedbackText = text });
        await db.ProgressReports.Where(x => x.Id == id && x.Status == "SUBMITTED").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "REVIEWED").SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public Task<MeetingDto?> GetMeetingAsync(long id, CancellationToken ct) => MeetingQuery(db.Meetings.Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
    public async Task<PagedResult<MeetingDto>> ListMeetingsAsync(long projectId, MeetingListFilter f, CancellationToken ct)
    {
        var q = db.Meetings.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(x => x.Status == f.Status);
        if (f.From.HasValue) q = q.Where(x => x.StartAt >= f.From.Value);
        if (f.To.HasValue) q = q.Where(x => x.StartAt <= f.To.Value);
        var count = await q.CountAsync(ct); var items = await MeetingQuery(q.OrderByDescending(x => x.StartAt).Skip((f.PageNumber - 1) * f.PageSize).Take(f.PageSize)).ToListAsync(ct);
        return new(items, f.PageNumber, f.PageSize, count, (int)Math.Ceiling(count / (double)f.PageSize));
    }

    public async Task<long> CreateMeetingAsync(CreateMeetingData d, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var meeting = new Meeting { ProjectId = d.ProjectId, CreatedBy = d.ActorId, Title = d.Title, Agenda = d.Agenda,
            StartAt = d.StartAt, EndAt = d.EndAt, Location = d.Location, OnlineUrl = d.OnlineUrl, Status = "SCHEDULED" };
        db.Meetings.Add(meeting); await db.SaveChangesAsync(ct);
        foreach (var userId in d.ParticipantIds) meeting.MeetingParticipants.Add(new MeetingParticipant { UserId = userId, AttendanceStatus = "INVITED" });
        if (d.ParticipantIds.Count > 0)
        {
            var notification = new Notification { CreatedBy = d.ActorId, NotificationType = "MEETING_SCHEDULED", Title = d.Title,
                Content = $"Meeting scheduled for {d.StartAt:u}.", RelatedEntityType = "MEETING", RelatedEntityId = meeting.Id };
            foreach (var userId in d.ParticipantIds) notification.NotificationRecipients.Add(new NotificationRecipient { UserId = userId });
            db.Notifications.Add(notification);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return meeting.Id;
    }

    public async Task<bool> UpdateScheduledMeetingAsync(long id, UpdateMeetingData d, CancellationToken ct) =>
        await db.Meetings.Where(x => x.Id == id && x.Status == "SCHEDULED").ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, d.Title)
            .SetProperty(x => x.Agenda, d.Agenda).SetProperty(x => x.StartAt, d.StartAt).SetProperty(x => x.EndAt, d.EndAt)
            .SetProperty(x => x.Location, d.Location).SetProperty(x => x.OnlineUrl, d.OnlineUrl).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) == 1;
    public async Task<bool> CancelMeetingAsync(long id, CancellationToken ct) => await db.Meetings.Where(x => x.Id == id && x.Status == "SCHEDULED")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "CANCELLED").SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) == 1;
    public async Task<bool> UpdateMinutesAsync(long id, string? notes, bool complete, CancellationToken ct)
    {
        var query = db.Meetings.Where(x => x.Id == id && x.Status != "CANCELLED");
        var updated = complete
            ? await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.MeetingNotes, notes).SetProperty(x => x.Status, "COMPLETED").SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct)
            : await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.MeetingNotes, notes).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
        return updated == 1;
    }
    public async Task<bool> SetAttendanceAsync(long meetingId, long userId, string status, CancellationToken ct) => await db.MeetingParticipants.Where(x => x.MeetingId == meetingId && x.UserId == userId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.AttendanceStatus, status).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) == 1;
    public async Task ReplaceParticipantsAsync(long meetingId, long actorId, string title, IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.MeetingParticipants.Where(x => x.MeetingId == meetingId).Select(x => x.UserId).ToListAsync(ct);
        await db.MeetingParticipants.Where(x => x.MeetingId == meetingId && !userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
        var added = userIds.Except(existing).ToArray();
        db.MeetingParticipants.AddRange(added.Select(x => new MeetingParticipant { MeetingId = meetingId, UserId = x, AttendanceStatus = "INVITED" }));
        if (added.Length > 0)
        {
            var notification = new Notification { CreatedBy = actorId, NotificationType = "MEETING_INVITATION", Title = title,
                Content = "You were invited to a project meeting.", RelatedEntityType = "MEETING", RelatedEntityId = meetingId };
            foreach (var id in added) notification.NotificationRecipients.Add(new NotificationRecipient { UserId = id });
            db.Notifications.Add(notification);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task AddDecisionAsync(long meetingId, long actorId, string content, CancellationToken ct)
    { db.Set<ExtensionModels.MeetingDecision>().Add(new() { MeetingId = meetingId, CreatedBy = actorId, Content = content }); await db.SaveChangesAsync(ct); }
    public async Task AddBlockerAsync(long meetingId, long actorId, string content, CancellationToken ct)
    { db.Set<ExtensionModels.MeetingBlocker>().Add(new() { MeetingId = meetingId, CreatedBy = actorId, Content = content }); await db.SaveChangesAsync(ct); }
    public Task<bool> IsTaskInProjectAsync(long taskId, long projectId, CancellationToken ct) => db.Tasks.AnyAsync(x => x.Id == taskId && x.Milestone.ProjectId == projectId, ct);
    public Task<bool> IsMilestoneInProjectAsync(long milestoneId, long projectId, CancellationToken ct) => db.Milestones.AnyAsync(x => x.Id == milestoneId && x.ProjectId == projectId, ct);
    public async Task AddActionItemAsync(CreateActionItemData d, CancellationToken ct)
    { db.Set<ExtensionModels.MeetingActionItem>().Add(new() { MeetingId = d.MeetingId, CreatedBy = d.ActorId, Title = d.Title, Description = d.Description, OwnerUserId = d.OwnerUserId, DueDate = d.DueDate, Status = d.Status, TaskId = d.TaskId, MilestoneId = d.MilestoneId }); await db.SaveChangesAsync(ct); }
    public async Task<bool> UpdateActionItemStatusAsync(long meetingId, long actionItemId, string status, CancellationToken ct) =>
        await db.Set<ExtensionModels.MeetingActionItem>().Where(x => x.Id == actionItemId && x.MeetingId == meetingId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) == 1;
    public async Task AddMeetingFeedbackAsync(long id, long projectId, long assignmentId, string text, CancellationToken ct)
    { db.SupervisorFeedbacks.Add(new SupervisorFeedback { ProjectId = projectId, SupervisorAssignmentId = assignmentId, MeetingId = id, FeedbackText = text }); await db.SaveChangesAsync(ct); }

    private IQueryable<ProgressReportDto> ReportQuery(IQueryable<ProgressReport> source) => source.AsNoTracking().Select(x => new ProgressReportDto(x.Id, x.ProjectId, x.SubmittedBy,
        x.ReportType, x.PeriodStart, x.PeriodEnd, x.Summary, x.CompletedWork, x.PlannedWork, x.IssuesAndRisks, x.Status, x.SubmittedAt,
        x.CreatedAt, x.UpdatedAt,
        db.Set<ExtensionModels.ProgressReportMetadata>().Where(m => m.ReportId == x.Id).Select(m => (long?)m.ReportPeriodId).FirstOrDefault(),
        (from m in db.Set<ExtensionModels.ProgressReportMetadata>() join p in db.Set<ExtensionModels.ProgressReportPeriod>() on m.ReportPeriodId equals p.Id where m.ReportId == x.Id select (DateTime?)p.DeadlineAt).FirstOrDefault(),
        (from m in db.Set<ExtensionModels.ProgressReportMetadata>() join p in db.Set<ExtensionModels.ProgressReportPeriod>() on m.ReportPeriodId equals p.Id where m.ReportId == x.Id select p.LatePolicy).FirstOrDefault(),
        db.Set<ExtensionModels.ProgressReportMetadata>().Where(m => m.ReportId == x.Id).Select(m => m.IsLate).FirstOrDefault(),
        db.Set<ExtensionModels.ProgressReportSection>().Where(s => s.ReportId == x.Id).OrderBy(s => s.Id).Select(s => new ReportSectionDto(s.SectionType, s.Content)).ToList(),
        db.Set<ExtensionModels.ProgressReportContribution>().Where(c => c.ReportId == x.Id).OrderBy(c => c.CreatedAt).Select(c => new ReportContributionDto(c.Id, c.ContributorId, c.SectionType, c.Content, c.CreatedAt)).ToList(),
        x.SupervisorFeedbacks.OrderBy(f => f.CreatedAt).Select(f => new FeedbackDto(f.Id, f.SupervisorAssignmentId, f.FeedbackText, f.CreatedAt)).ToList()));
    private IQueryable<MeetingDto> MeetingQuery(IQueryable<Meeting> source) => source.AsNoTracking().Select(x => new MeetingDto(x.Id, x.ProjectId, x.Title, x.Agenda,
        x.MeetingNotes, x.StartAt, x.EndAt, x.Location, x.OnlineUrl, x.Status, x.CreatedBy, x.CreatedAt, x.UpdatedAt,
        x.MeetingParticipants.OrderBy(p => p.Id).Select(p => new MeetingParticipantDto(p.UserId, p.User.FullName, p.AttendanceStatus)).ToList(),
        db.Set<ExtensionModels.MeetingDecision>().Where(d => d.MeetingId == x.Id).OrderBy(d => d.Id).Select(d => new MeetingTextItemDto(d.Id, d.Content, d.CreatedBy, d.CreatedAt)).ToList(),
        db.Set<ExtensionModels.MeetingBlocker>().Where(b => b.MeetingId == x.Id).OrderBy(b => b.Id).Select(b => new MeetingTextItemDto(b.Id, b.Content, b.CreatedBy, b.CreatedAt)).ToList(),
        db.Set<ExtensionModels.MeetingActionItem>().Where(a => a.MeetingId == x.Id).OrderBy(a => a.Id).Select(a => new MeetingActionItemDto(a.Id, a.Title, a.Description, a.OwnerUserId, a.DueDate, a.Status, a.TaskId, a.MilestoneId, a.CreatedBy, a.CreatedAt)).ToList(),
        x.SupervisorFeedbacks.OrderBy(f => f.CreatedAt).Select(f => new FeedbackDto(f.Id, f.SupervisorAssignmentId, f.FeedbackText, f.CreatedAt)).ToList()));
}
