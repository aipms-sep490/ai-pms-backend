using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AIPMS.Application.Common.Exceptions;
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
        return ConversationDto(row);
    }

    public async Task<IReadOnlyList<AssistantConversationDto>> ConversationsAsync(long? projectId, long? beforeId, CancellationToken ct)
    {
        var actor = await ActiveUserAsync(ct);
        if (projectId.HasValue) await projectContext.RetrieveProjectContextAsync(projectId.Value, "history", ct);
        var rows = await db.Set<AssistantConversation>().AsNoTracking()
            .Where(x => x.ActorId == actor.Id && x.ProjectId == projectId && x.DepartmentId == actor.DepartmentId && x.DeletedAt == null && (!beforeId.HasValue || x.Id < beforeId))
            .OrderByDescending(x => x.Id).Take(20).ToArrayAsync(ct);
        return rows.Select(ConversationDto).ToArray();
    }

    public async Task<AssistantConversationDetailDto> ConversationAsync(long id, long? beforeTurnId, CancellationToken ct)
    {
        var conversation = await OwnedConversationAsync(id, ct);
        var rows = await (from turn in db.Set<AssistantConversationTurn>().AsNoTracking()
                          join run in db.AcademicKnowledgeQueryRuns.AsNoTracking() on turn.RunId equals run.Id
                          where turn.ConversationId == id && (!beforeTurnId.HasValue || turn.Id < beforeTurnId)
                          orderby turn.Id descending
                          select new { turn.Id, turn.Query, turn.RunId, run.ResultJson }).Take(21).ToArrayAsync(ct);
        return new(ConversationDto(conversation), rows.Take(20).Reverse().Select(x => new AssistantTurnDto(x.Id, x.Query,
            JsonSerializer.Deserialize<AcademicAssistantResponseDto>(x.ResultJson)! with { RunId = x.RunId })).ToArray(), rows.Length > 20);
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

    private sealed record MemoryTurn(string Query, string Answer);
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
            // Official/project facts must be retrieved again under today's access and publication state.
            var answer = result.AnswerType is "GENERAL" or "ATTACHMENT" ? result.Answer : "[Previous sourced answer omitted; retrieve current authorized sources.]";
            if (row.Query.Length + answer.Length > budget) break;
            memory.Add(new(row.Query, answer)); budget -= row.Query.Length + answer.Length;
        }
        memory.Reverse(); return memory.ToArray();
    }

    private static AssistantConversationDto ConversationDto(AssistantConversation x) => new(x.Id, x.Title, x.ProjectId, x.Scope, x.Revision, x.UpdatedAt);

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
