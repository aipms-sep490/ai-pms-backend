using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class MeetingNotesHistory
{
    // Caller holds the meeting lock; the snapshot and its audit commit with the mutation.
    public static async System.Threading.Tasks.Task Capture(AipmsDbContext db, M.Meeting meeting, long actor, DateTime now, CancellationToken ct)
    {
        var decisions = await db.MeetingDecisions.AsNoTracking().Where(x => x.MeetingId == meeting.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Content, x.DecidedBy, x.DecidedAt }).Take(1001).ToArrayAsync(ct);
        var actions = await db.MeetingActionItems.AsNoTracking().Where(x => x.MeetingId == meeting.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Description, x.AssigneeUserId, x.DueAt }).Take(1001).ToArrayAsync(ct);
        var projectActions = await db.ProjectActionItems.AsNoTracking().Where(x => x.MeetingId == meeting.Id)
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Description, x.OwnerId, x.TaskId, x.MilestoneId, x.DueAt }).Take(1001).ToArrayAsync(ct);
        if (decisions.Length > 1000 || actions.Length > 1000 || projectActions.Length > 1000)
            throw new ConflictException("Notes history supports at most 1000 decisions/actions per collection.");
        meeting.NotesVersion++;
        db.Set<MeetingNotesVersion>().Add(new() { MeetingId = meeting.Id, Version = meeting.NotesVersion,
            IsLocked = meeting.NotesLockedAt.HasValue, MeetingNotes = meeting.MeetingNotes, Minutes = meeting.Minutes,
            Decisions = meeting.Decisions, Blockers = meeting.Blockers, ChangedBy = actor, ChangedAt = now,
            GovernanceJson = JsonSerializer.Serialize(new { Decisions = decisions, Actions = actions, ProjectActions = projectActions }) });
        await db.SaveChangesAsync(ct);
    }
}
