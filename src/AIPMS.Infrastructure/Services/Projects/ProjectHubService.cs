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

internal sealed class ProjectHubService(
    AipmsDbContext db,
    ICurrentUser current,
    IAuditTrail audit,
    TimeProvider clock) : IProjectHubService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private sealed record ProjectAccessContext(
        M.Project Project,
        bool IsAdmin,
        bool IsScopedStaff,
        bool IsPrimarySupervisor,
        bool IsMentor,
        bool IsTeamMember,
        bool IsTeamLeader,
        string? ActorMajorCode,
        string? ActorMajorName);

    private static bool MatchesChannelDiscipline(string scope, string? majorCode, string? majorName)
    {
        var combined = $"{majorCode ?? ""} {majorName ?? ""}".ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(combined)) return false;
        var isMkt = combined.Contains("MKT") || combined.Contains("MARKETING") || combined.Contains("BRAND")
            || combined.Contains("COMMUNICATION") || combined.Contains("DIGITAL MEDIA");
        if (scope == "MKT") return isMkt;
        if (scope == "IT") return !isMkt && (combined.Contains("IT") || combined.Contains("SE") || combined.Contains("SOFTWARE")
            || combined.Contains("CS") || combined.Contains("COMPUTER") || combined.Contains("IS") || combined.Contains("CNTT"));
        return true;
    }

    private async Task<ProjectAccessContext> LoadAccessAsync(long projectId, CancellationToken ct)
    {
        var actorId = Actor;
        var project = await db.Projects
            .Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .Include(p => p.ProjectMajors).ThenInclude(pm => pm.Major)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var user = await db.Users.AsNoTracking()
            .Include(u => u.Major)
            .SingleOrDefaultAsync(u => u.Id == actorId && u.Status == "ACTIVE", ct)
            ?? throw new ForbiddenException();

        var isAdmin = current.Roles.Contains(AppRoles.Admin);
        var projectDeptIds = project.ProjectMajors.Select(pm => pm.Major.DepartmentId).Distinct().ToHashSet();
        var isScopedStaff = current.Roles.Contains(AppRoles.DepartmentStaff)
            && user.DepartmentId.HasValue && projectDeptIds.Contains(user.DepartmentId.Value);

        var supAssigns = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && a.SupervisorProfile.UserId == actorId && a.EndedAt == null)
            .Select(a => new { a.IsPrimary, a.AssignmentType }).ToArrayAsync(ct);
        var isPrimary = supAssigns.Any(a => a.IsPrimary || a.AssignmentType == "PRIMARY");
        var isMentor = supAssigns.Length > 0 && !isPrimary;

        var member = project.Team.TeamMembers.FirstOrDefault(m => m.UserId == actorId && m.LeftAt == null);
        return new(project, isAdmin, isScopedStaff, isPrimary, isMentor, member is not null, member?.IsLeader == true,
            user.Major?.Code, user.Major?.Name);
    }

    private static void RequireProjectMemberOrStaff(ProjectAccessContext ctx)
    {
        if (!ctx.IsAdmin && !ctx.IsScopedStaff && !ctx.IsPrimarySupervisor && !ctx.IsMentor && !ctx.IsTeamMember)
            throw new ForbiddenException("You do not have access to this project workspace.");
    }

    private static void RequireExecutiveOrSupervisorScope(ProjectAccessContext ctx)
    {
        if (!ctx.IsAdmin && !ctx.IsScopedStaff && !ctx.IsPrimarySupervisor && !ctx.IsMentor)
            throw new ForbiddenException("Only authorized Department Staff, Supervisors, or Administrators can view executive pulse and audit timeline.");
    }

    private static string NormalizeChannelScope(string scope)
    {
        var normalized = scope?.Trim().ToUpperInvariant() ?? "";
        if (normalized is not ("GENERAL" or "IT" or "MKT"))
            throw new DomainException("Channel scope must be GENERAL, IT, or MKT.", WorkflowErrorCodes.ValidationFailed);
        return normalized;
    }

    private static void RequireChannelAccess(ProjectAccessContext ctx, string channelScope)
    {
        RequireProjectMemberOrStaff(ctx);
        if (channelScope == "GENERAL") return;
        if (ctx.IsAdmin || ctx.IsPrimarySupervisor || ctx.IsTeamLeader || ctx.IsScopedStaff) return;
        if (MatchesChannelDiscipline(channelScope, ctx.ActorMajorCode, ctx.ActorMajorName)) return;
        throw new ForbiddenException($"You do not have access to the '{channelScope}' discipline channel.", WorkflowErrorCodes.ScopeAccessDenied);
    }

    public async Task<SpecVersionDto> CreateSpecVersionAsync(long projectId, long deliverableId, CreateSpecVersionRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var deliverable = await db.Deliverables.SingleOrDefaultAsync(d => d.Id == deliverableId && d.ProjectId == projectId, ct)
            ?? throw new NotFoundException("Deliverable", deliverableId);

        var maxVer = await db.DeliverableVersions.Where(v => v.DeliverableId == deliverableId)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;
        var nextVer = maxVer + 1;
        var changelog = input.Changelog?.Trim();
        if (nextVer >= 2 && (string.IsNullOrWhiteSpace(changelog) || changelog.Length < 10))
            throw new DomainException("Revision versions (v2+) require a changelog of at least 10 characters.", WorkflowErrorCodes.ValidationFailed);

        var now = Now;
        var version = new M.DeliverableVersion
        {
            DeliverableId = deliverable.Id,
            VersionNumber = nextVer,
            SubmittedBy = Actor,
            SubmissionNote = input.SubmissionNote?.Trim(),
            Changelog = changelog,
            Status = "SUBMITTED",
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.DeliverableVersions.Add(version);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "SPEC_VERSION_CREATED", "DELIVERABLE_VERSION", version.Id, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["deliverableId"] = deliverableId,
            ["versionNumber"] = version.VersionNumber
        }), ct);

        return new(version.Id, version.DeliverableId, version.VersionNumber, version.Status,
            version.SubmissionNote, version.Changelog, version.SubmittedBy, version.SubmittedAt);
    }

    public async Task<SpecVersionCompareDto> CompareSpecVersionsAsync(long projectId, long deliverableId, int fromVersion, int toVersion, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var versions = await db.DeliverableVersions.AsNoTracking()
            .Include(v => v.Files)
            .Where(v => v.DeliverableId == deliverableId && v.Deliverable.ProjectId == projectId
                && (v.VersionNumber == fromVersion || v.VersionNumber == toVersion))
            .ToArrayAsync(ct);

        var from = versions.FirstOrDefault(v => v.VersionNumber == fromVersion)
            ?? throw new NotFoundException("DeliverableVersion", fromVersion);
        var to = versions.FirstOrDefault(v => v.VersionNumber == toVersion)
            ?? throw new NotFoundException("DeliverableVersion", toVersion);

        var fromSet = from.Files.Select(f => $"{f.OriginalFileName}:{f.ChecksumSha256}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toSet = to.Files.Select(f => $"{f.OriginalFileName}:{f.ChecksumSha256}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = to.Files.Where(f => !fromSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();
        var removed = from.Files.Where(f => !toSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();
        var unchanged = to.Files.Where(f => fromSet.Contains($"{f.OriginalFileName}:{f.ChecksumSha256}")).Select(f => f.OriginalFileName).ToArray();

        static SpecVersionDto Map(M.DeliverableVersion v) =>
            new(v.Id, v.DeliverableId, v.VersionNumber, v.Status, v.SubmissionNote, v.Changelog, v.SubmittedBy, v.SubmittedAt);
        return new(deliverableId, Map(from), Map(to), added, removed, unchanged);
    }

    public async Task<IReadOnlyList<PinnedResourceDto>> ListPinnedResourcesAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        return await db.Set<ProjectPinnedResource>().AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new PinnedResourceDto(r.Id, r.ProjectId, r.ResourceType, r.Title, r.Url, r.CreatedBy, r.CreatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<PinnedResourceDto> AddPinnedResourceAsync(long projectId, CreatePinnedResourceRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var type = input.ResourceType?.Trim().ToUpperInvariant() ?? "OTHER";
        if (type is not ("FIGMA" or "DRIVE" or "DOC" or "REPO_LINK" or "OTHER"))
            throw new DomainException("Resource type must be FIGMA, DRIVE, DOC, REPO_LINK, or OTHER.", WorkflowErrorCodes.ValidationFailed);
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Url))
            throw new DomainException("Pinned resource title and URL are required.", WorkflowErrorCodes.ValidationFailed);

        var item = new ProjectPinnedResource
        {
            ProjectId = projectId,
            ResourceType = type,
            Title = input.Title.Trim(),
            Url = input.Url.Trim(),
            CreatedBy = Actor,
            CreatedAt = Now
        };
        db.Set<ProjectPinnedResource>().Add(item);
        await db.SaveChangesAsync(ct);
        return new(item.Id, item.ProjectId, item.ResourceType, item.Title, item.Url, item.CreatedBy, item.CreatedAt);
    }

    public async Task DeletePinnedResourceAsync(long projectId, long resourceId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var item = await db.Set<ProjectPinnedResource>().SingleOrDefaultAsync(r => r.Id == resourceId && r.ProjectId == projectId, ct)
            ?? throw new NotFoundException("ProjectPinnedResource", resourceId);
        db.Set<ProjectPinnedResource>().Remove(item);
        await db.SaveChangesAsync(ct);
    }

    private async Task<M.ChatConversation> GetOrCreateChannelConversationAsync(long projectId, string channelScope, CancellationToken ct)
    {
        var conv = await db.Set<M.ChatConversation>()
            .SingleOrDefaultAsync(c => c.ProjectId == projectId && c.ChannelScope == channelScope, ct);
        if (conv is not null) return conv;

        var now = Now;
        conv = new M.ChatConversation
        {
            Kind = "PROJECT",
            ProjectId = projectId,
            Status = "OPEN",
            ChannelScope = channelScope,
            Sequence = 0,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Set<M.ChatConversation>().Add(conv);
        await db.SaveChangesAsync(ct);
        return conv;
    }

    public async Task<HubChannelDto> GetChannelAsync(long projectId, string channelScope, CancellationToken ct)
    {
        var scope = NormalizeChannelScope(channelScope);
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireChannelAccess(ctx, scope);

        var conv = await GetOrCreateChannelConversationAsync(projectId, scope, ct);
        var messages = await db.Set<M.ChatMessage>().AsNoTracking()
            .Where(m => m.ConversationId == conv.Id && m.RecalledAt == null)
            .OrderBy(m => m.Sequence).Take(100)
            .Select(m => new HubChannelMessageDto(m.Id, m.ConversationId, scope, m.Sequence, m.SenderId, m.Body ?? "", m.CreatedAt))
            .ToArrayAsync(ct);
        return new(conv.Id, projectId, scope, messages);
    }

    public async Task<HubChannelMessageDto> PostChannelMessageAsync(long projectId, string channelScope, PostChannelMessageRequest input, CancellationToken ct)
    {
        var scope = NormalizeChannelScope(channelScope);
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireChannelAccess(ctx, scope);
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Trim().Length > 4000)
            throw new DomainException("Message body must be 1..4000 characters.", WorkflowErrorCodes.ValidationFailed);

        var conv = await GetOrCreateChannelConversationAsync(projectId, scope, ct);
        var now = Now;
        conv.Sequence += 1;
        conv.Version += 1;
        conv.UpdatedAt = now;
        var body = input.Body.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{conv.Id}|{Actor}|{body}|{conv.Sequence}"))).ToLowerInvariant();
        var msg = new M.ChatMessage
        {
            ConversationId = conv.Id,
            Sequence = conv.Sequence,
            SenderId = Actor,
            ClientMessageId = Guid.NewGuid(),
            RequestHash = hash,
            Body = body,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.Set<M.ChatMessage>().Add(msg);
        await db.SaveChangesAsync(ct);
        return new(msg.Id, msg.ConversationId, scope, msg.Sequence, msg.SenderId, msg.Body ?? "", msg.CreatedAt);
    }

    public async Task<ChatConvertedTaskDto> ConvertMessageToTaskAsync(long projectId, long messageId, ConvertMessageToTaskRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);

        var message = await (from m in db.Set<M.ChatMessage>().AsNoTracking()
                             join c in db.Set<M.ChatConversation>().AsNoTracking() on m.ConversationId equals c.Id
                             where m.Id == messageId && c.ProjectId == projectId
                             select new { m.Id, m.Body, c.ChannelScope }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ChatMessage", messageId);
        if (!string.IsNullOrWhiteSpace(message.ChannelScope))
            RequireChannelAccess(ctx, message.ChannelScope);

        // Idempotent replay check: return existing task without duplicating
        var existing = await db.Tasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.SourceMessageId == messageId && t.DeletedAt == null, ct);
        if (existing is not null)
            return new(existing.Id, existing.MilestoneId, messageId, existing.Title, existing.Status, AlreadyExisted: true);

        long milestoneId;
        if (input.MilestoneId.HasValue)
        {
            if (!await db.Milestones.AsNoTracking().AnyAsync(m => m.Id == input.MilestoneId.Value && m.ProjectId == projectId, ct))
                throw new NotFoundException("Milestone", input.MilestoneId.Value);
            milestoneId = input.MilestoneId.Value;
        }
        else
        {
            var latest = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.Id).Select(m => (long?)m.Id).FirstOrDefaultAsync(ct);
            if (!latest.HasValue)
                throw new DomainException("At least one project milestone is required before converting a chat message into a task.", WorkflowErrorCodes.MilestoneNotInProject);
            milestoneId = latest.Value;
        }

        var rawTitle = !string.IsNullOrWhiteSpace(input.Title) ? input.Title.Trim() : (message.Body ?? "Chat action task").Trim();
        var title = rawTitle.Length <= 200 ? rawTitle : rawTitle[..200];
        var now = Now;
        var task = new M.Task
        {
            MilestoneId = milestoneId,
            Title = title,
            Description = message.Body,
            Status = "TODO",
            Priority = "MEDIUM",
            CreatedBy = Actor,
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyToken = Guid.NewGuid(),
            SourceMessageId = messageId
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(Actor, "CHAT_CONVERTED_TO_TASK", "TASK", task.Id, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["sourceMessageId"] = messageId
        }), ct);
        return new(task.Id, task.MilestoneId, messageId, task.Title, task.Status, AlreadyExisted: false);
    }

    public async Task<ChatConvertedDecisionDto> ConvertMessageToDecisionAsync(long projectId, long messageId, ConvertMessageToDecisionRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);

        var message = await (from m in db.Set<M.ChatMessage>().AsNoTracking()
                             join c in db.Set<M.ChatConversation>().AsNoTracking() on m.ConversationId equals c.Id
                             where m.Id == messageId && c.ProjectId == projectId
                             select new { m.Id, m.Body, c.ChannelScope }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ChatMessage", messageId);
        if (!string.IsNullOrWhiteSpace(message.ChannelScope))
            RequireChannelAccess(ctx, message.ChannelScope);

        var existing = await db.MeetingDecisions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.SourceMessageId == messageId, ct);
        if (existing is not null)
            return new(existing.Id, existing.MeetingId, messageId, existing.Content, AlreadyExisted: true);

        long meetingId;
        if (input.MeetingId.HasValue)
        {
            if (!await db.Meetings.AsNoTracking().AnyAsync(m => m.Id == input.MeetingId.Value && m.ProjectId == projectId, ct))
                throw new NotFoundException("Meeting", input.MeetingId.Value);
            meetingId = input.MeetingId.Value;
        }
        else
        {
            var latest = await db.Meetings.AsNoTracking().Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.Id).Select(m => (long?)m.Id).FirstOrDefaultAsync(ct);
            if (!latest.HasValue)
                throw new DomainException("At least one project meeting is required before converting a chat message into a meeting decision.", WorkflowErrorCodes.ValidationFailed);
            meetingId = latest.Value;
        }

        var content = !string.IsNullOrWhiteSpace(input.Content) ? input.Content.Trim() : (message.Body ?? "").Trim();
        var now = Now;
        var decision = new MeetingDecision
        {
            MeetingId = meetingId,
            Content = content,
            DecidedBy = Actor,
            DecidedAt = now,
            CreatedAt = now,
            SourceMessageId = messageId
        };
        db.MeetingDecisions.Add(decision);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(Actor, "CHAT_CONVERTED_TO_DECISION", "MEETING_DECISION", decision.Id, new Dictionary<string, object?>
        {
            ["projectId"] = projectId,
            ["sourceMessageId"] = messageId
        }), ct);
        return new(decision.Id, decision.MeetingId, messageId, decision.Content, AlreadyExisted: false);
    }

    public async Task<IReadOnlyList<AnchoredCommentDto>> ListAnchoredCommentsAsync(long projectId, string gateCode, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var gateId = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == projectId && g.Code == gateCode)
            .Select(g => (long?)g.Id).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectGate", 0);

        return await db.Set<EvidenceAnchoredComment>().AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.GateInstanceId == gateId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new AnchoredCommentDto(c.Id, c.ProjectId, c.GateInstanceId, c.EvidenceId,
                c.AnchorRef, c.Severity, c.Status, c.Content, c.CreatedBy, c.ResolvedBy, c.CreatedAt, c.ResolvedAt))
            .ToArrayAsync(ct);
    }

    public async Task<AnchoredCommentDto> CreateAnchoredCommentAsync(long projectId, string gateCode, CreateAnchoredCommentRequest input, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var gateId = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == projectId && g.Code == gateCode)
            .Select(g => (long?)g.Id).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectGate", 0);

        var severity = input.Severity?.Trim().ToUpperInvariant() ?? "INFO";
        if (severity is not ("INFO" or "WARNING" or "CRITICAL"))
            throw new DomainException("Severity must be INFO, WARNING, or CRITICAL.", WorkflowErrorCodes.ValidationFailed);
        if (string.IsNullOrWhiteSpace(input.AnchorRef) || string.IsNullOrWhiteSpace(input.Content))
            throw new DomainException("Anchor reference and comment content are required.", WorkflowErrorCodes.ValidationFailed);

        var comment = new EvidenceAnchoredComment
        {
            ProjectId = projectId,
            GateInstanceId = gateId,
            EvidenceId = input.EvidenceId,
            AnchorRef = input.AnchorRef.Trim(),
            Severity = severity,
            Status = "OPEN",
            Content = input.Content.Trim(),
            CreatedBy = Actor,
            CreatedAt = Now
        };
        db.Set<EvidenceAnchoredComment>().Add(comment);
        await db.SaveChangesAsync(ct);
        return new(comment.Id, comment.ProjectId, comment.GateInstanceId, comment.EvidenceId,
            comment.AnchorRef, comment.Severity, comment.Status, comment.Content, comment.CreatedBy, comment.ResolvedBy, comment.CreatedAt, comment.ResolvedAt);
    }

    public async Task<AnchoredCommentDto> ResolveAnchoredCommentAsync(long projectId, string gateCode, long commentId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireProjectMemberOrStaff(ctx);
        var gateId = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => g.ProjectId == projectId && g.Code == gateCode)
            .Select(g => (long?)g.Id).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectGate", 0);

        var comment = await db.Set<EvidenceAnchoredComment>()
            .SingleOrDefaultAsync(c => c.Id == commentId && c.ProjectId == projectId && c.GateInstanceId == gateId, ct)
            ?? throw new NotFoundException("EvidenceAnchoredComment", commentId);
        comment.Status = "RESOLVED";
        comment.ResolvedBy = Actor;
        comment.ResolvedAt = Now;
        await db.SaveChangesAsync(ct);
        return new(comment.Id, comment.ProjectId, comment.GateInstanceId, comment.EvidenceId,
            comment.AnchorRef, comment.Severity, comment.Status, comment.Content, comment.CreatedBy, comment.ResolvedBy, comment.CreatedAt, comment.ResolvedAt);
    }

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
        RequireProjectMemberOrStaff(ctx);
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
        RequireProjectMemberOrStaff(ctx);
        return await db.Set<EvidenceRubricCriterionLink>().AsNoTracking()
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new EvidenceRubricLinkDto(l.Id, l.ProjectId, l.EvidenceId, l.RubricCriterionId, l.CreatedBy, l.CreatedAt))
            .ToArrayAsync(ct);
    }

    public async Task<ProjectHealthPulseDto> GetHealthPulseAsync(long projectId, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireExecutiveOrSupervisorScope(ctx);
        var now = Now;

        var gateReqs = await db.ProjectGateRequirements.AsNoTracking()
            .Include(r => r.Evidence)
            .Where(r => r.GateInstance.ProjectId == projectId && r.Required && r.VerificationStatus != "WAIVED_WITHDRAWN")
            .ToArrayAsync(ct);
        var totalMandatory = gateReqs.Length;
        var readyCount = gateReqs.Count(r => r.Evidence.OrderByDescending(e => e.Version).FirstOrDefault()?.ScanStatus == "READY");
        decimal? evidenceReadyPct = totalMandatory == 0 ? null : decimal.Round(readyCount * 100m / totalMandatory, 2);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.Milestone.ProjectId == projectId)
            .Select(t => new { t.Status, t.BlockedSinceAt, t.TaskCategory }).ToArrayAsync(ct);
        var blockedCutoff = now.AddHours(-48);
        var blockedOver48h = tasks.Count(t => t.Status == "BLOCKED" && t.BlockedSinceAt.HasValue && t.BlockedSinceAt.Value <= blockedCutoff);

        var reportCutoff = now.AddDays(-14);
        var totalReports = await db.ProgressReports.AsNoTracking().CountAsync(r => r.ProjectId == projectId, ct);
        var recentReports = await db.ProgressReports.AsNoTracking()
            .CountAsync(r => r.ProjectId == projectId && r.Status != "DRAFT" && (r.SubmittedAt ?? r.UpdatedAt) >= reportCutoff, ct);

        decimal? balanceRatio = null;
        var isInter = string.Equals(ctx.Project.TrackCode, "IT_MKT", StringComparison.OrdinalIgnoreCase)
            || ctx.Project.ProjectMajors.Count > 1;
        if (isInter)
        {
            var itCount = tasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("IT_", StringComparison.OrdinalIgnoreCase));
            var mktCount = tasks.Count(t => t.TaskCategory != null && t.TaskCategory.StartsWith("MKT_", StringComparison.OrdinalIgnoreCase));
            if (itCount + mktCount > 0)
            {
                var max = Math.Max(itCount, mktCount);
                var min = Math.Min(itCount, mktCount);
                balanceRatio = decimal.Round((decimal)min / max, 2);
            }
        }

        var signals = new HealthPulseSignalsDto(evidenceReadyPct, totalMandatory, blockedOver48h, recentReports, balanceRatio);

        string status;
        string summary;
        if (totalMandatory == 0 && tasks.Length == 0 && totalReports == 0)
        {
            status = "INSUFFICIENT_DATA";
            summary = "Chưa đủ tín hiệu vận hành (chưa có Gate, Task hoặc Báo cáo tuần) để kết luận sức khỏe đồ án.";
        }
        else if (blockedOver48h >= 2 || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 50m) || (balanceRatio.HasValue && balanceRatio.Value < 0.25m))
        {
            status = "RED";
            summary = "Đồ án có nguy cơ cao: tồn đọng Task BLOCKED > 48h, tỷ lệ minh chứng READY thấp hoặc lệch nhịp IT/MKT.";
        }
        else if (blockedOver48h == 1 || (evidenceReadyPct.HasValue && evidenceReadyPct.Value < 80m) || recentReports == 0 || (balanceRatio.HasValue && balanceRatio.Value < 0.50m))
        {
            status = "YELLOW";
            summary = "Đồ án cần theo dõi: kiểm tra báo cáo tuần, minh chứng còn thiếu hoặc tiến độ liên ngành.";
        }
        else
        {
            status = "GREEN";
            summary = "Tất cả 4 tín hiệu thực thi đang ổn định.";
        }

        return new(projectId, status, summary, signals, now);
    }

    public async Task<IReadOnlyList<ProjectAuditTimelineEntryDto>> GetAuditTimelineAsync(long projectId, int limit, CancellationToken ct)
    {
        var ctx = await LoadAccessAsync(projectId, ct);
        RequireExecutiveOrSupervisorScope(ctx);
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
