using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Domain.Meetings;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class MeetingGovernanceService(AipmsDbContext db, ICurrentUser user,
    IProjectAccessService access, IAuditTrail audit, TimeProvider clock) : IMeetingGovernanceService
{
    private long Actor => user.UserId ?? throw new UnauthorizedException();

    private static void Page(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue)
            throw Invalid("Invalid pagination: page >= 1, pageSize 1..100.");
    }

    private async Task<M.Meeting> Read(long id, CancellationToken ct)
    {
        var actor = Actor;
        var meeting = await db.Meetings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Meeting", id);
        if (!await access.CanAccessAsync(actor, meeting.ProjectId, ct)) throw new ForbiddenException();
        return meeting;
    }

    public async Task<PagedResult<MeetingDecisionDto>> Decisions(long meetingId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        await Read(meetingId, ct);
        var q = db.MeetingDecisions.AsNoTracking().Where(x => x.MeetingId == meetingId);
        var count = await q.LongCountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(DecisionDto).ToArray(), page, pageSize, count);
    }

    public async Task<PagedResult<MeetingActionItemDto>> Actions(long meetingId, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        await Read(meetingId, ct);
        var q = db.MeetingActionItems.AsNoTracking().Where(x => x.MeetingId == meetingId);
        var count = await q.LongCountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(ActionDto).ToArray(), page, pageSize, count);
    }

    private async Task<M.Meeting> Lock(long id, CancellationToken ct)
    {
        var initial = await Read(id, ct);
        var teamId = await db.Projects.Where(x => x.Id == initial.ProjectId).Select(x => x.TeamId).SingleAsync(ct);
        await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={teamId}").SingleAsync(ct);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={initial.ProjectId}").SingleAsync(ct);
        var meeting = await db.Meetings.FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").SingleAsync(ct);
        var active = await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct);
        var member = await db.TeamMembers.AnyAsync(x => x.TeamId == teamId && x.UserId == Actor && x.LeftAt == null, ct);
        var supervisor = await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == project.Id && x.EndedAt == null && x.SupervisorProfile.UserId == Actor, ct);
        var leader = await db.TeamMembers.AnyAsync(x => x.TeamId == teamId && x.UserId == Actor && x.LeftAt == null && x.IsLeader, ct);
        if (!active || !(supervisor || leader || (member && meeting.CreatedBy == Actor))) throw new ForbiddenException();
        if (project.Status != "ACTIVE" || !MeetingGovernanceRules.CanManageActions(meeting.Status))
            throw new ConflictException("The project or meeting is read-only.");
        return meeting;
    }

    private static void Current(Guid current, string supplied)
    {
        if (!Guid.TryParse(supplied, out var token) || token != current)
            throw new ConflictException("The resource changed. Reload before saving.");
    }

    public async Task<MeetingDecisionDto> Decide(long meetingId, CreateMeetingDecisionRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Content) || input.Content.Length > 4000)
            throw Invalid("Decision content must contain 1..4000 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var meeting = await Lock(meetingId, ct);
        if (!MeetingGovernanceRules.CanRecordDecision(meeting.Status)) throw new ConflictException("Complete the meeting before recording decisions.");
        Current(meeting.ConcurrencyToken, input.ConcurrencyToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new MeetingDecision { MeetingId = meetingId, Content = input.Content.Trim(), DecidedBy = Actor, DecidedAt = now, CreatedAt = now };
        db.MeetingDecisions.Add(row);
        meeting.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "MEETING_DECISION_CREATED", "MEETING_DECISION", row.Id,
            new Dictionary<string, object?> { ["meetingId"] = meetingId, ["projectId"] = meeting.ProjectId }), ct);
        await tx.CommitAsync(ct);
        return DecisionDto(row);
    }

    public async Task<MeetingActionItemDto> SaveAction(long meetingId, long? actionId, SaveMeetingActionItemRequest input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 500 || input.Description?.Length > 4000
            || !MeetingGovernanceRules.IsActionStatus(input.Status)) throw Invalid("Invalid action item input.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var meeting = await Lock(meetingId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        MeetingActionItem row;
        if (actionId.HasValue)
        {
            row = await db.MeetingActionItems.SingleOrDefaultAsync(x => x.Id == actionId && x.MeetingId == meetingId, ct)
                ?? throw new NotFoundException("Meeting action item", actionId.Value);
            Current(row.ConcurrencyToken, input.ConcurrencyToken);
            if (!MeetingGovernanceRules.CanTransitionAction(row.Status, input.Status)) throw new ConflictException("Completed or cancelled actions cannot be reopened.");
        }
        else
        {
            Current(meeting.ConcurrencyToken, input.ConcurrencyToken);
            if (input.Status != "OPEN") throw new ConflictException("New actions must be OPEN.");
            row = new() { MeetingId = meetingId, CreatedBy = Actor, CreatedAt = now };
            db.MeetingActionItems.Add(row);
        }
        if (input.AssigneeUserId.HasValue)
        {
            var assignee = input.AssigneeUserId.Value;
            var team = await db.Projects.Where(p => p.Id == meeting.ProjectId).Select(p => p.TeamId).SingleAsync(ct);
            if (!await db.Users.AnyAsync(u => u.Id == assignee && u.Status == "ACTIVE", ct)
                || !(await db.TeamMembers.AnyAsync(m => m.TeamId == team && m.UserId == assignee && m.LeftAt == null, ct)
                    || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == meeting.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == assignee, ct)))
                throw new ForbiddenException("The assignee must be an active member or supervisor of this project.");
        }
        row.Title = input.Title.Trim(); row.Description = input.Description?.Trim(); row.AssigneeUserId = input.AssigneeUserId;
        row.DueAt = input.DueAt; row.Status = input.Status; row.UpdatedAt = now; row.ConcurrencyToken = Guid.NewGuid();
        meeting.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, actionId.HasValue ? "MEETING_ACTION_UPDATED" : "MEETING_ACTION_CREATED", "MEETING_ACTION_ITEM", row.Id,
            new Dictionary<string, object?> { ["meetingId"] = meetingId, ["projectId"] = meeting.ProjectId, ["status"] = row.Status, ["assigneeUserId"] = row.AssigneeUserId }), ct);
        if (row.AssigneeUserId is { } recipientId && recipientId != Actor)
        {
            var notification = new M.Notification { CreatedBy = Actor, NotificationType = "MEETING_ACTION_ASSIGNED",
                Title = "Meeting action updated", Content = row.Title, RelatedEntityType = "MEETING_ACTION_ITEM",
                RelatedEntityId = row.Id, CreatedAt = now, UpdatedAt = now };
            notification.NotificationRecipients.Add(new() { UserId = recipientId, CreatedAt = now, UpdatedAt = now });
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return ActionDto(row);
    }

    private static MeetingDecisionDto DecisionDto(MeetingDecision x) => new(x.Id, x.MeetingId, x.Content, x.DecidedBy, x.DecidedAt);
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["input"] = [message] });
    private static MeetingActionItemDto ActionDto(MeetingActionItem x) => new(x.Id, x.MeetingId, x.Title, x.Description,
        x.AssigneeUserId, x.DueAt, x.Status, x.ConcurrencyToken.ToString("N"), x.CreatedBy, x.CreatedAt, x.UpdatedAt);
}
