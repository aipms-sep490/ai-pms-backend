using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Application.Features.Chat;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Chat;

internal sealed class ChatAccessService(AipmsDbContext db, TimeProvider clock) : IChatAccessService
{
    internal IQueryable<ChatScopeMember> LiveScopes => db.Set<ChatScopeMember>().Where(s =>
        s.Kind == "TEAM" ? db.Teams.Any(t => t.Id == s.ScopeId && t.Status != "DISBANDED" && t.Status != "ARCHIVED" && t.Status != "CLOSED")
        : db.Projects.Any(p => p.Id == s.ScopeId && p.Status != "COMPLETED" && p.Status != "ARCHIVED" && p.Status != "CANCELLED"));

    internal IQueryable<long> Contacts(long actor) => from a in LiveScopes where a.UserId == actor
        join b in LiveScopes on new { a.Kind, a.ScopeId } equals new { b.Kind, b.ScopeId }
        where b.UserId != actor select b.UserId;

    internal Task<bool> Active(long actor, CancellationToken ct) => db.Users.AnyAsync(u => u.Id == actor && u.Status == "ACTIVE"
        && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
        && (u.LockoutEndAt == null || u.LockoutEndAt <= clock.GetUtcNow().UtcDateTime), ct);

    internal IQueryable<ChatConversation> Allowed(long actor) => db.Set<ChatConversation>().Where(c =>
        c.Status == "READ_ONLY" ? db.Set<ChatMembershipInterval>().Any(m => m.ConversationId == c.Id && m.UserId == actor && m.LeftAt == null && m.Retained)
        : c.Kind == "DIRECT" ? (c.FirstUserId == actor || c.SecondUserId == actor) && Contacts(actor).Contains(c.FirstUserId == actor ? c.SecondUserId!.Value : c.FirstUserId!.Value)
        : LiveScopes.Any(s => s.UserId == actor && s.Kind == c.Kind && s.ScopeId == (c.Kind == "TEAM" ? c.TeamId : c.ProjectId)));

    public async Task<bool> CanAccessAsync(long userId, long conversationId, CancellationToken ct) =>
        await Active(userId, ct) && await Allowed(userId).AnyAsync(c => c.Id == conversationId, ct);

    internal async Task<Dictionary<long,string>> Eligible(ChatConversation c, CancellationToken ct)
    {
        if (c.Status == "READ_ONLY") return await db.Set<ChatMembershipInterval>().Where(m => m.ConversationId == c.Id && m.LeftAt == null && m.Retained)
            .ToDictionaryAsync(m => m.UserId, m => m.SourceKey, ct);
        if (c.Kind == "DIRECT")
        {
            var sources = await (from a in LiveScopes where a.UserId == c.FirstUserId
                join b in LiveScopes on new { a.Kind, a.ScopeId } equals new { b.Kind, b.ScopeId }
                where b.UserId == c.SecondUserId select a.SourceKey + "/" + b.SourceKey).Distinct().ToListAsync(ct);
            if (sources.Count == 0) return [];
            var key = Hash(sources);
            return new() { [c.FirstUserId!.Value] = key, [c.SecondUserId!.Value] = key };
        }
        var rows = await LiveScopes.Where(s => s.Kind == c.Kind && s.ScopeId == (c.Kind == "TEAM" ? c.TeamId : c.ProjectId)).ToListAsync(ct);
        return rows.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => Hash(g.Select(s => s.SourceKey)));
    }

    public async Task<IReadOnlyList<long>> RecipientsAsync(long conversationId, CancellationToken ct)
    {
        var c = await db.Set<ChatConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (c is null) return [];
        var ids = (await Eligible(c, ct)).Keys.ToArray();
        return await db.Users.Where(u => ids.Contains(u.Id) && u.Status == "ACTIVE"
            && (u.LockoutEndAt == null || u.LockoutEndAt <= clock.GetUtcNow().UtcDateTime)).Select(u => u.Id).ToListAsync(ct);
    }

    private static string Hash(IEnumerable<string> parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts.Order(StringComparer.Ordinal)))));
}
