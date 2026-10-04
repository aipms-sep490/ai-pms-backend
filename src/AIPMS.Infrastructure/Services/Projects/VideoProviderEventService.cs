using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using AsyncTask = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class VideoProviderEventService(AipmsDbContext db, LiveKitWebhookVerifier verifier, TimeProvider clock) : IVideoProviderEventService
{
    public async AsyncTask ProcessLiveKitAsync(string rawBody, string? authorization, CancellationToken ct)
    {
        if (!verifier.Verify(authorization, rawBody)) throw new UnauthorizedException("Invalid provider signature.");
        using var doc = JsonDocument.Parse(rawBody); var root = doc.RootElement;
        var eventId = root.TryGetProperty("id", out var id) ? id.GetString() : null; var type = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(type)) throw new ValidationException(new Dictionary<string, string[]> { ["event"] = ["Provider event id and type are required."] });
        if (await db.VideoProviderEvents.AnyAsync(x => x.Provider == "LIVEKIT" && x.ProviderEventId == eventId, ct)) return;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var roomKey = ReadRoom(root); var session = roomKey == null ? null : await db.MeetingVideoSessions.FirstOrDefaultAsync(x => x.ProviderRoomKey == roomKey, ct);
        var inbox = new VideoProviderEvent { Provider = "LIVEKIT", ProviderEventId = eventId, EventType = type, MeetingVideoSessionId = session?.Id, PayloadHash = hash, ProcessingStatus = "PROCESSING", ReceivedAt = clock.GetUtcNow().UtcDateTime };
        db.VideoProviderEvents.Add(inbox);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { return; }
        if (session != null) await ApplyAsync(session, type, root, ct);
        inbox.ProcessingStatus = "PROCESSED"; inbox.ProcessedAt = clock.GetUtcNow().UtcDateTime; await db.SaveChangesAsync(ct);
    }

    private async AsyncTask ApplyAsync(MeetingVideoSession session, string type, JsonElement root, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (type == "room_started" && session.Status == "CREATED") { session.Status = "LIVE"; session.StartedAt ??= now; session.UpdatedAt = now; }
        if (type == "room_finished") { session.Status = "ENDED"; session.EndedAt ??= now; session.UpdatedAt = now; await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == session.Id && x.LeftAt == null).ExecuteUpdateAsync(x => x.SetProperty(p => p.LeftAt, now).SetProperty(p => p.UpdatedAt, now), ct); }
        if (type is "participant_joined" or "participant_left" or "participant_connection_aborted")
        {
            var identity = ReadParticipant(root); if (identity == null) return;
            var binding = await db.MeetingVideoParticipantBindings.FirstOrDefaultAsync(x => x.MeetingVideoSessionId == session.Id && x.ProviderParticipantIdentity == identity, ct); if (binding == null) return;
            if (type == "participant_joined") db.MeetingVideoPresenceSessions.Add(new MeetingVideoPresenceSession { MeetingVideoSessionId = session.Id, UserId = binding.UserId, ProviderParticipantIdentity = identity, ProviderConnectionId = ReadConnection(root), JoinedAt = now, CreatedAt = now, UpdatedAt = now });
            else { var open = await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == session.Id && x.UserId == binding.UserId && x.LeftAt == null).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct); if (open != null) { open.LeftAt = now; open.DisconnectReason = type; open.UpdatedAt = now; } }
        }
        await db.SaveChangesAsync(ct);
    }

    private static string? ReadRoom(JsonElement root) => root.TryGetProperty("room", out var room) && room.TryGetProperty("name", out var name) ? name.GetString() : null;
    private static string? ReadParticipant(JsonElement root) => root.TryGetProperty("participant", out var p) && p.TryGetProperty("identity", out var id) ? id.GetString() : null;
    private static string? ReadConnection(JsonElement root) => root.TryGetProperty("participant", out var p) && p.TryGetProperty("sid", out var id) ? id.GetString() : null;
}


