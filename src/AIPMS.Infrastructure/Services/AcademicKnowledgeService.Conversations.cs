using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services;

internal sealed partial class AcademicKnowledgeService
{
    public async Task<AssistantConversationDto> CreateConversationAsync(CreateAssistantConversationRequest request, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        if (request.Scope is not ("ACADEMIC" or "PROJECT" or "COMBINED") || request.Scope != "ACADEMIC" && request.ProjectId == null)
            throw new ArgumentException("Invalid conversation scope.");
        if (request.ProjectId.HasValue) await projectContext.RetrieveProjectContextAsync(request.ProjectId.Value, "conversation", ct);
        var row = new AssistantConversation { ActorId = actor.Id, DepartmentId = actor.DepartmentId, ProjectId = request.ProjectId,
            Scope = request.Scope, UpdatedAt = clock.GetUtcNow().UtcDateTime };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(actor.Id, "ASSISTANT_CONVERSATION_CREATED", "ASSISTANT_CONVERSATION", row.Id, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
        return ConversationDto(row, 0);
    }

    public async Task<IReadOnlyList<AssistantConversationDto>> ConversationsAsync(long? projectId, long? beforeId, CancellationToken ct, string? search = null)
    {
        var actor = await ActiveUserAsync(ct);
        if (projectId.HasValue) await projectContext.RetrieveProjectContextAsync(projectId.Value, "history", ct);
        var trimmedSearch = search?.Trim();
        var query = db.Set<AssistantConversation>().AsNoTracking()
            .Where(x => x.ActorId == actor.Id && x.ProjectId == projectId && x.DepartmentId == actor.DepartmentId && x.DeletedAt == null);
        if (beforeId.HasValue)
            query = query.Where(x => x.Id < beforeId.Value);
        if (!string.IsNullOrWhiteSpace(trimmedSearch))
            query = query.Where(x => x.Title.Contains(trimmedSearch));

        var rows = await query
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .Take(25)
            .ToArrayAsync(ct);

        var ids = rows.Select(r => r.Id).ToArray();
        var turnCounts = ids.Length == 0
            ? new Dictionary<long, int>()
            : await db.Set<AssistantConversationTurn>().AsNoTracking()
                .Where(t => ids.Contains(t.ConversationId))
                .GroupBy(t => t.ConversationId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return rows.Select(x => ConversationDto(x, turnCounts.GetValueOrDefault(x.Id, 0))).ToArray();
    }

    public async Task<AssistantConversationDetailDto> ConversationAsync(long id, long? beforeTurnId, CancellationToken ct)
    {
        var conversation = await OwnedConversationAsync(id, ct);
        var rows = await (from turn in db.Set<AssistantConversationTurn>().AsNoTracking()
                          join run in db.AcademicKnowledgeQueryRuns.AsNoTracking() on turn.RunId equals run.Id
                          where turn.ConversationId == id && (!beforeTurnId.HasValue || turn.Id < beforeTurnId)
                          orderby turn.Id descending
                          select new { turn.Id, turn.Query, turn.RunId, run.StartedAt, run.ResultJson, run.FeedbackRating }).Take(21).ToArrayAsync(ct);
        var totalTurns = await db.Set<AssistantConversationTurn>().AsNoTracking().CountAsync(t => t.ConversationId == id, ct);
        return new(
            ConversationDto(conversation, totalTurns),
            rows.Take(20).Reverse().Select(x => new AssistantTurnDto(
                x.Id,
                x.Query,
                x.StartedAt,
                JsonSerializer.Deserialize<AcademicAssistantResponseDto>(x.ResultJson)! with
                {
                    RunId = x.RunId,
                    FeedbackRating = x.FeedbackRating
                })).ToArray(),
            rows.Length > 20);
    }

    public async Task<AssistantConversationDto> UpdateConversationAsync(long id, UpdateAssistantConversationRequest request, CancellationToken ct)
    {
        var row = await OwnedConversationAsync(id, ct);
        var newTitle = request.Title?.Trim();
        if (request.Title is not null && (string.IsNullOrWhiteSpace(newTitle) || newTitle.Length > 120))
            throw new ArgumentException("Tiêu đề hội thoại phải từ 1 đến 120 ký tự.");
        if (newTitle is null && request.IsPinned is null)
            throw new ArgumentException("Cần cung cấp tiêu đề mới hoặc trạng thái ghim.");

        var effectiveTitle = newTitle ?? row.Title;
        var effectivePinned = request.IsPinned ?? row.IsPinned;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.Set<AssistantConversation>()
            .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Assistant conversation", id);
        entity.Title = effectiveTitle;
        entity.IsPinned = effectivePinned;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(row.ActorId, "ASSISTANT_CONVERSATION_UPDATED", "ASSISTANT_CONVERSATION", id,
            new Dictionary<string, object?> { ["pinned"] = effectivePinned, ["renamed"] = newTitle is not null }), ct);
        await tx.CommitAsync(ct);
        var turnCount = await db.Set<AssistantConversationTurn>().AsNoTracking().CountAsync(t => t.ConversationId == id, ct);
        return new(row.Id, effectiveTitle, row.ProjectId, row.Scope, row.Revision, row.UpdatedAt, effectivePinned, turnCount);
    }

    public async Task<AssistantExport> ExportConversationAsync(long conversationId, string format, CancellationToken ct)
    {
        var normalized = (format ?? "md").Trim().ToLowerInvariant();
        if (normalized is not ("txt" or "md" or "docx" or "pdf"))
            throw new ArgumentException("Hỗ trợ xuất hội thoại định dạng Markdown (md), TXT, Word (docx) và PDF (pdf).");

        var conversation = await OwnedConversationAsync(conversationId, ct);
        var rows = await (from turn in db.Set<AssistantConversationTurn>().AsNoTracking()
                          join run in db.AcademicKnowledgeQueryRuns.AsNoTracking() on turn.RunId equals run.Id
                          where turn.ConversationId == conversationId
                          orderby turn.Id ascending
                          select new { turn.Id, turn.Query, turn.RunId, run.StartedAt, run.ResultJson, run.FeedbackRating })
                          .Take(100)
                          .ToArrayAsync(ct);

        var turns = rows.Select(x => new AssistantTurnDto(
            x.Id,
            x.Query,
            x.StartedAt,
            JsonSerializer.Deserialize<AcademicAssistantResponseDto>(x.ResultJson)! with
            {
                RunId = x.RunId,
                FeedbackRating = x.FeedbackRating
            })).ToArray();

        var file = AssistantDocumentExporter.CreateConversationExport(conversation.Title, turns, normalized);
        await audit.RecordAsync(new(conversation.ActorId, "ASSISTANT_CONVERSATION_EXPORTED", "ASSISTANT_CONVERSATION", conversationId,
            new Dictionary<string, object?> { ["format"] = normalized, ["turnCount"] = turns.Length }), ct);
        return file;
    }

    public async Task<AssistantFeedbackDto> SubmitFeedbackAsync(long runId, SubmitAssistantFeedbackRequest request, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        if (request.Rating is not (-1 or 1))
            throw new ArgumentException("Đánh giá phải là 1 (Hữu ích) hoặc -1 (Chưa đạt).");
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is not null && note.Length > 400)
            throw new ArgumentException("Ghi chú đánh giá tối đa 400 ký tự.");

        var run = await db.AcademicKnowledgeQueryRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId && x.ActorId == actor.Id, ct)
            ?? throw new NotFoundException("Assistant answer", runId);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.AcademicKnowledgeQueryRuns
            .SingleOrDefaultAsync(x => x.Id == run.Id && x.ActorId == actor.Id, ct)
            ?? throw new NotFoundException("Assistant answer", runId);
        entity.FeedbackRating = request.Rating;
        entity.FeedbackNote = note;
        entity.FeedbackAt = now;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(actor.Id, "ACADEMIC_ASSISTANT_FEEDBACK", "KNOWLEDGE_QUERY_RUN", run.Id,
            new Dictionary<string, object?> { ["rating"] = request.Rating }), ct);
        await tx.CommitAsync(ct);
        return new(run.Id, request.Rating, note, now);
    }

    public async Task<IReadOnlyList<AssistantProjectOptionDto>> AccessibleProjectsAsync(CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        var roles = await db.UserRoles.AsNoTracking()
            .Where(r => r.UserId == actor.Id)
            .Select(r => r.Role.Code)
            .ToArrayAsync(ct);

        var results = new Dictionary<long, AssistantProjectOptionDto>();

        // 1. Student projects (active team membership)
        if (roles.Contains(AppRoles.Student))
        {
            var studentProjects = await db.TeamMembers.AsNoTracking()
                .Where(m => m.UserId == actor.Id && m.LeftAt == null && m.Team.Project != null)
                .Select(m => new
                {
                    m.Team.Project!.Id,
                    m.Team.Project.Code,
                    m.Team.Project.Title,
                    m.Team.Project.Status,
                    m.IsLeader
                })
                .OrderByDescending(p => p.Id)
                .Take(20)
                .ToArrayAsync(ct);
            foreach (var p in studentProjects)
                results.TryAdd(p.Id, new(p.Id, p.Code, p.Title, p.Status, p.IsLeader ? "Trưởng nhóm" : "Thành viên"));
        }

        // 2. Lecturer / Supervisor / Mentor projects
        if (roles.Contains(AppRoles.Lecturer))
        {
            var supProjects = await db.SupervisorAssignments.AsNoTracking()
                .Where(a => a.EndedAt == null && a.SupervisorProfile.UserId == actor.Id)
                .Select(a => new
                {
                    a.Project.Id,
                    a.Project.Code,
                    a.Project.Title,
                    a.Project.Status,
                    a.AssignmentType
                })
                .OrderByDescending(p => p.Id)
                .Take(40)
                .ToArrayAsync(ct);
            foreach (var p in supProjects)
            {
                var label = p.AssignmentType == "PRIMARY" ? "GVHD chính" : "Mentor chuyên ngành";
                results.TryAdd(p.Id, new(p.Id, p.Code, p.Title, p.Status, label));
            }
        }

        // 3. Department staff projects (within own department)
        if (roles.Contains(AppRoles.DepartmentStaff) && actor.DepartmentId.HasValue)
        {
            var deptId = actor.DepartmentId.Value;
            var deptProjects = await db.Projects.AsNoTracking()
                .Where(p => p.ProjectMajors.Any(pm => pm.Major.DepartmentId == deptId))
                .OrderByDescending(p => p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION")
                .ThenByDescending(p => p.Id)
                .Select(p => new { p.Id, p.Code, p.Title, p.Status })
                .Take(40)
                .ToArrayAsync(ct);
            foreach (var p in deptProjects)
                results.TryAdd(p.Id, new(p.Id, p.Code, p.Title, p.Status, "Cán bộ bộ môn"));
        }

        // 4. Admin overview projects
        if (roles.Contains(AppRoles.Admin))
        {
            var adminProjects = await db.Projects.AsNoTracking()
                .OrderByDescending(p => p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION")
                .ThenByDescending(p => p.Id)
                .Select(p => new { p.Id, p.Code, p.Title, p.Status })
                .Take(40)
                .ToArrayAsync(ct);
            foreach (var p in adminProjects)
                results.TryAdd(p.Id, new(p.Id, p.Code, p.Title, p.Status, "Quản trị viên"));
        }

        return results.Values
            .OrderByDescending(p => p.Status is "ACTIVE" or "FINAL_SUBMISSION")
            .ThenByDescending(p => p.Id)
            .Take(50)
            .ToArray();
    }

    public async Task DeleteConversationAsync(long id, CancellationToken ct)
    {
        var row = await OwnedConversationAsync(id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Set<AssistantConversation>().Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeletedAt, clock.GetUtcNow().UtcDateTime).SetProperty(x => x.Revision, x => x.Revision + 1), ct);
        await db.Set<AssistantConversationTurn>().Where(x => x.ConversationId == id).ExecuteDeleteAsync(ct);
        await audit.RecordAsync(new(row.ActorId, "ASSISTANT_CONVERSATION_DELETED", "ASSISTANT_CONVERSATION", id, new Dictionary<string, object?>()), ct);
        await tx.CommitAsync(ct);
    }

    public async Task<KnowledgeCitationDto> CitationAsync(long runId, int index, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        var run = await db.AcademicKnowledgeQueryRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.ActorId == actor.Id, ct)
            ?? throw new NotFoundException("Assistant source", runId);
        if (run.ProjectId.HasValue) await projectContext.RetrieveProjectContextAsync(run.ProjectId.Value, "citation", ct);
        var result = JsonSerializer.Deserialize<AcademicAssistantResponseDto>(run.ResultJson)!;
        if (result.ConversationId.HasValue) await OwnedConversationAsync(result.ConversationId.Value, ct);
        if (index < 0 || index >= result.Citations.Count) throw new NotFoundException("Citation", index);
        // Return only the exact cited snapshot, never an arbitrary version or a whole private file.
        return result.Citations[index];
    }

    private async Task<AssistantConversation> OwnedConversationAsync(long id, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        var row = await db.Set<AssistantConversation>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ActorId == actor.Id && x.DepartmentId == actor.DepartmentId && x.DeletedAt == null, ct)
            ?? throw new NotFoundException("Assistant conversation", id);
        if (row.ProjectId.HasValue) await projectContext.RetrieveProjectContextAsync(row.ProjectId.Value, "history", ct);
        return row;
    }

    private sealed record MemoryTurn(string Query, string Answer, bool IsMemorySummaryOnly);
    private async Task<MemoryTurn[]> MemoryAsync(long id, CancellationToken ct)
    {
        var rows = await (from turn in db.Set<AssistantConversationTurn>().AsNoTracking()
                          join run in db.AcademicKnowledgeQueryRuns.AsNoTracking() on turn.RunId equals run.Id
                          where turn.ConversationId == id && run.Status == "COMPLETED"
                          orderby turn.Id descending
                          select new { turn.Query, run.ResultJson }).Take(8).ToArrayAsync(ct);
        var memory = new List<MemoryTurn>();
        var budget = 16000;
        foreach (var row in rows)
        {
            var result = JsonSerializer.Deserialize<AcademicAssistantResponseDto>(row.ResultJson)!;
            var isGeneral = result.AnswerType is "GENERAL" or "ATTACHMENT";
            // Keep a bounded summary of prior sourced turns so follow-up references ("mục 2 ở trên",
            // "3 task vừa nêu") stay coherent while forcing re-verification against live authorized sources.
            var answer = isGeneral
                ? result.Answer
                : SummarizeMemoryTurn(result.Answer);
            if (row.Query.Length + answer.Length > budget) break;
            memory.Add(new(row.Query, answer, !isGeneral));
            budget -= row.Query.Length + answer.Length;
        }
        memory.Reverse();
        return memory.ToArray();
    }

    private static string SummarizeMemoryTurn(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return "[Tóm tắt lượt trước: không có nội dung; hãy đối chiếu lại nguồn hiện hành.]";
        var compact = Regex.Replace(answer.Trim(), @"\s+", " ");
        if (compact.Length > 550) compact = compact[..550] + "…";
        return $"[Tóm tắt ngữ cảnh tham chiếu — KHÔNG dùng làm căn cứ chính thức, bắt buộc đối chiếu lại sources hiện hành]: {compact}";
    }

    private static AssistantConversationDto ConversationDto(AssistantConversation x, int turnCount = 0) =>
        new(x.Id, x.Title, x.ProjectId, x.Scope, x.Revision, x.UpdatedAt, x.IsPinned, turnCount);

    internal static string? PartialAnswer(string raw)
    {
        var match = Regex.Match(raw, "\\\"answer\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)");
        if (!match.Success) return null;
        var value = match.Groups[1].Value;
        // A chunk may end inside a JSON unicode escape. Wait for its remaining bytes.
        for (var trim = 0; trim <= Math.Min(6, value.Length); trim++)
        {
            try { return JsonSerializer.Deserialize<string>("\"" + value[..(value.Length - trim)] + "\""); }
            catch (JsonException) { }
        }
        return null;
    }
}
