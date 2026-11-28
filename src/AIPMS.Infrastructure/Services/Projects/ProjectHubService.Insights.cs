using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProjectHub;
using AIPMS.Application.Features.ProjectHub.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ProjectHubService
{
    public async Task<InternalContributionHeatmapDto> GetInternalHeatmapAsync(long projectId, int weeks, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var clampedWeeks = Math.Clamp(weeks, 1, 12);
        var now = Now;
        var windowStart = now.AddDays(-7 * clampedWeeks);

        var members = await (from tm in db.TeamMembers.AsNoTracking()
                             join u in db.Users.AsNoTracking() on tm.UserId equals u.Id
                             where tm.TeamId == ctx.Project.TeamId && tm.LeftAt == null
                             orderby u.FullName
                             select new { tm.UserId, u.FullName, u.MajorId }).ToArrayAsync(ct);
        var memberIds = members.Select(m => m.UserId).ToArray();

        // Collect purely internal activity timestamps (ZERO Git sync!)
        var taskCreated = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == projectId && memberIds.Contains(t.CreatedBy))
            .Select(t => new { UserId = t.CreatedBy, At = t.CreatedAt }).ToArrayAsync(ct);
        var statusChanges = await db.TaskStatusHistories.AsNoTracking()
            .Where(h => h.Task.DeletedAt == null && h.Task.Milestone.ProjectId == projectId && memberIds.Contains(h.ChangedBy))
            .Select(h => new { UserId = h.ChangedBy, At = h.ChangedAt }).ToArrayAsync(ct);
        var reports = await db.ProgressReports.AsNoTracking()
            .Where(r => r.ProjectId == projectId && memberIds.Contains(r.SubmittedBy))
            .Select(r => new { UserId = r.SubmittedBy, At = r.SubmittedAt ?? r.UpdatedAt }).ToArrayAsync(ct);
        var gateEvidences = await db.Set<GateEvidence>().AsNoTracking()
            .Where(e => e.ProjectId == projectId && memberIds.Contains(e.SubmittedBy))
            .Select(e => new { UserId = e.SubmittedBy, At = e.SubmittedAt }).ToArrayAsync(ct);
        var chatMsgs = await (from m in db.Set<M.ChatMessage>().AsNoTracking()
                              join c in db.Set<M.ChatConversation>().AsNoTracking() on m.ConversationId equals c.Id
                              where c.ProjectId == projectId && memberIds.Contains(m.SenderId) && m.RecalledAt == null
                              select new { UserId = m.SenderId, At = m.CreatedAt }).ToArrayAsync(ct);

        var allEvents = taskCreated.Concat(statusChanges).Concat(reports).Concat(gateEvidences).Concat(chatMsgs)
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.At).ToArray());

        var rows = new List<MemberWeeklyActivityDto>(members.Length);
        var silentIds = new List<long>();
        foreach (var member in members)
        {
            var events = allEvents.GetValueOrDefault(member.UserId) ?? [];
            var buckets = new int[clampedWeeks];
            foreach (var at in events)
            {
                if (at < windowStart || at > now) continue;
                var daysAgo = (int)Math.Floor((now - at).TotalDays);
                var weekFromEnd = Math.Clamp(daysAgo / 7, 0, clampedWeeks - 1);
                var bucketIndex = (clampedWeeks - 1) - weekFromEnd;
                buckets[bucketIndex]++;
            }
            DateTime? lastAt = events.Length == 0 ? null : events.Max();
            int? daysSince = lastAt.HasValue ? (int)Math.Floor((now - lastAt.Value).TotalDays) : null;
            var isSilent = !lastAt.HasValue || (now - lastAt.Value).TotalDays > 10.0;
            if (isSilent) silentIds.Add(member.UserId);
            rows.Add(new(member.UserId, member.FullName, member.MajorId, buckets, buckets.Sum(), lastAt, daysSince, isSilent));
        }

        return new(projectId, clampedWeeks, now, rows, silentIds);
    }

    public async Task<EvidenceRubricLinkDto> LinkEvidenceToRubricAsync(long projectId, LinkEvidenceToRubricRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireGateReviewOrProjectAccess(ctx, ctx.IsGateReviewer);
        RequireActiveProject(ctx);
        if (!await db.Set<GateEvidence>().AsNoTracking().AnyAsync(e => e.Id == input.EvidenceId && e.ProjectId == projectId, ct))
            throw new NotFoundException("GateEvidence", input.EvidenceId);
        if (!await db.RubricCriteria.AsNoTracking().AnyAsync(c => c.Id == input.RubricCriterionId, ct))
            throw new NotFoundException("RubricCriterion", input.RubricCriterionId);

        var existing = await db.Set<EvidenceRubricCriterionLink>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.EvidenceId == input.EvidenceId && l.RubricCriterionId == input.RubricCriterionId, ct);
        if (existing is not null)
            return new(existing.Id, existing.ProjectId, existing.EvidenceId, existing.RubricCriterionId, existing.CreatedBy, existing.CreatedAt);

        var link = new EvidenceRubricCriterionLink
        {
            ProjectId = projectId,
            EvidenceId = input.EvidenceId,
            RubricCriterionId = input.RubricCriterionId,
            CreatedBy = Actor,
            CreatedAt = Now
        };
        db.Set<EvidenceRubricCriterionLink>().Add(link);
        await db.SaveChangesAsync(ct);
        return new(link.Id, link.ProjectId, link.EvidenceId, link.RubricCriterionId, link.CreatedBy, link.CreatedAt);
    }

    public async Task<IReadOnlyList<EvidenceRubricLinkDto>> ListEvidenceRubricLinksAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireGateReviewOrProjectAccess(ctx, ctx.IsGateReviewer);
        return await db.Set<EvidenceRubricCriterionLink>().AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new EvidenceRubricLinkDto(l.Id, l.ProjectId, l.EvidenceId, l.RubricCriterionId, l.CreatedBy, l.CreatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<ProjectHealthPulseDto> GetHealthPulseAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireHealthPulseScope(ctx);
        var calculator = healthPulseCalculator ?? new ProjectHealthPulseCalculator(db);
        return await calculator.CalculateAsync(projectId, Now, ct);
    }

    public async Task<IReadOnlyList<ProjectAuditTimelineEntryDto>> GetAuditTimelineAsync(long projectId, int limit, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireAuditTimelineScope(ctx);
        var take = Math.Clamp(limit, 1, 200);
        var pid = projectId.ToString();
        var marker = $"\"projectId\":{projectId}";
        return await db.AuditLogs.AsNoTracking()
            .Where(a => (a.EntityType == "PROJECT" && a.EntityId == pid)
                || (a.DetailsJson != null && a.DetailsJson.Contains(marker)))
            .OrderByDescending(a => a.OccurredAt)
            .Take(take)
            .Select(a => new ProjectAuditTimelineEntryDto(a.Id, a.ActorUserId, a.Action, a.EntityType, a.EntityId, a.Outcome, a.OccurredAt))
            .ToArrayAsync(ct);
    }
}
