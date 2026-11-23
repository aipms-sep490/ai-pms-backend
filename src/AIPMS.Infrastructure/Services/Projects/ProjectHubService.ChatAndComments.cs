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

        if (severity == "CRITICAL")
        {
            var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, ct);
            await V4WorkflowNotificationHelper.EmitAsync(
                db, Actor, "CRITICAL_ANCHORED_COMMENT_ADDED",
                $"Có bình luận nghiêm trọng (CRITICAL) trên minh chứng cổng {gateCode}",
                $"Vị trí {comment.AnchorRef}: cần xử lý và đóng bình luận CRITICAL trước khi nộp lại cổng {gateCode}.",
                "PROJECT", projectId, studentIds, Now, enqueueEmail: false, ct);
        }

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
}
