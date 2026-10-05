using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;
using AsyncTask = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class VideoProviderEventService(AipmsDbContext db, LiveKitWebhookVerifier verifier, TimeProvider clock) : IVideoProviderEventService
{
    public async AsyncTask ProcessLiveKitAsync(string rawBody, string? authorization, CancellationToken ct)
    {
        if (!verifier.Verify(authorization, rawBody)) throw new UnauthorizedException("Invalid provider signature.");
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventId = Text(root, "id");
        var type = Text(root, "event");
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 255 || string.IsNullOrWhiteSpace(type) || type.Length > 80)
            throw new ValidationException(new Dictionary<string, string[]> { ["event"] = ["Valid provider event id and type are required."] });

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var roomKey = root.TryGetProperty("room", out var room) ? Text(room, "name") : null;
        // Serialize the session before the inbox. Commit deduplication and its projection together,
        // so a failed delivery can be retried without losing or duplicating presence evidence.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        var session = roomKey == null ? null : db.Database.IsSqlServer()
            ? await db.MeetingVideoSessions.FromSqlInterpolated($"SELECT * FROM dbo.meeting_video_sessions WITH (UPDLOCK,HOLDLOCK) WHERE provider_room_key={roomKey}").SingleOrDefaultAsync(ct)
            : await db.MeetingVideoSessions.SingleOrDefaultAsync(x => x.ProviderRoomKey == roomKey, ct);
        var inbox = db.Database.IsSqlServer()
            ? await db.VideoProviderEvents.FromSqlInterpolated($"SELECT * FROM dbo.video_provider_events WITH (UPDLOCK,HOLDLOCK) WHERE provider='LIVEKIT' AND provider_event_id={eventId}").SingleOrDefaultAsync(ct)
            : await db.VideoProviderEvents.SingleOrDefaultAsync(x => x.Provider == "LIVEKIT" && x.ProviderEventId == eventId, ct);
        if (inbox is not null && inbox.PayloadHash != hash)
            throw new ValidationException(new Dictionary<string, string[]> { ["event"] = ["Provider event id was already used for another payload."] });
        if (inbox?.ProcessingStatus == "PROCESSED")
        {
            if (tx is not null) await tx.CommitAsync(ct);
            return;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (inbox is null)
        {
            inbox = new() { Provider = "LIVEKIT", ProviderEventId = eventId, EventType = type,
                MeetingVideoSessionId = session?.Id, PayloadHash = hash, ReceivedAt = now };
            db.VideoProviderEvents.Add(inbox);
        }
        if (session is not null) inbox.ErrorCode = await ApplyAsync(session, type, root, now, ct);
        inbox.ProcessingStatus = "PROCESSED";
        inbox.ProcessedAt = now;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private async System.Threading.Tasks.Task<string?> ApplyAsync(MeetingVideoSession session, string type, JsonElement root, DateTime now, CancellationToken ct)
    {
        if (type is not ("room_started" or "room_finished" or "participant_joined" or "participant_left" or "participant_connection_aborted")) return null;
        var occurred = Timestamp(root, "createdAt", "created_at");
        // Protobuf JSON represents int64 timestamps as strings. Do not substitute receipt time
        // for missing/invalid evidence; acknowledge it with a safe diagnostic instead.
        var earliestSecond = new DateTimeOffset(DateTime.SpecifyKind(session.CreatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (occurred is null || occurred < DateTimeOffset.FromUnixTimeSeconds(earliestSecond).UtcDateTime || occurred > now.AddMinutes(5))
            return "VIDEO_EVENT_INVALID_TIMESTAMP";
        var at = occurred.Value < session.CreatedAt ? session.CreatedAt : occurred.Value;
        if (type == "room_started" && session.Status == "CREATED")
        {
            session.Status = "LIVE";
            session.StartedAt ??= at;
            session.UpdatedAt = now;
            session.ConcurrencyToken = Guid.NewGuid();
        }
        if (type == "room_finished")
        {
            session.Status = "ENDED";
            session.EndedAt ??= at;
            session.UpdatedAt = now;
            session.ConcurrencyToken = Guid.NewGuid();
            var rows = await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == session.Id).ToListAsync(ct);
            foreach (var row in rows) Close(row, session.EndedAt.Value, "room_finished", now);
        }
        if (type is not ("participant_joined" or "participant_left" or "participant_connection_aborted")) return null;
        if (!root.TryGetProperty("participant", out var participant)) return "VIDEO_EVENT_INVALID_PARTICIPANT";
        var identity = Text(participant, "identity");
        var connection = Text(participant, "sid");
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(connection) || connection.Length > 255)
            return "VIDEO_EVENT_INVALID_PARTICIPANT";
        var binding = await db.MeetingVideoParticipantBindings.SingleOrDefaultAsync(x => x.MeetingVideoSessionId == session.Id && x.ProviderParticipantIdentity == identity, ct);
        if (binding is null) return "VIDEO_EVENT_UNKNOWN_PARTICIPANT";
        var joined = Timestamp(participant, "joinedAtMs", "joined_at_ms", milliseconds: true)
            ?? Timestamp(participant, "joinedAt", "joined_at") ?? at;
        if (joined < DateTimeOffset.FromUnixTimeSeconds(earliestSecond).UtcDateTime || joined > occurred.Value.AddSeconds(1))
            return "VIDEO_EVENT_INVALID_TIMESTAMP";
        if (joined < session.CreatedAt) joined = session.CreatedAt;
        var boundary = session.EndedAt ?? (session.Status is "ENDED" or "FAILED" ? session.UpdatedAt : (DateTime?)null);
        var presence = await db.MeetingVideoPresenceSessions.Where(x => x.MeetingVideoSessionId == session.Id
            && x.UserId == binding.UserId && x.ProviderConnectionId == connection).OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (presence is null)
        {
            // An aborted connection does not prove that a participant ever joined.
            if (type == "participant_connection_aborted" || boundary is { } end && joined > end) return null;
            presence = new() { MeetingVideoSessionId = session.Id, UserId = binding.UserId,
                ProviderParticipantIdentity = identity, ProviderConnectionId = connection,
                JoinedAt = joined, CreatedAt = now, UpdatedAt = now };
            db.MeetingVideoPresenceSessions.Add(presence);
        }
        else
        {
            // A leave can arrive first. The later join refines that connection's start but
            // never clears its known end or closes a different reconnect's segment.
            presence.JoinedAt = joined < presence.JoinedAt ? joined : presence.JoinedAt;
            presence.UpdatedAt = now;
        }
        if (type is "participant_left" or "participant_connection_aborted") Close(presence, at, type, now);
        if (boundary is { } cutoff) Close(presence, cutoff, "session_ended", now);
        return null;
    }

    private static void Close(MeetingVideoPresenceSession presence, DateTime at, string reason, DateTime now)
    {
        var end = at < presence.JoinedAt ? presence.JoinedAt : at;
        if (presence.LeftAt is null || presence.LeftAt > end)
        {
            presence.LeftAt = end;
            presence.DisconnectReason = reason;
            presence.UpdatedAt = now;
        }
    }

    private static string? Text(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTime? Timestamp(JsonElement obj, string camel, string snake, bool milliseconds = false)
    {
        if (obj.ValueKind != JsonValueKind.Object || (!obj.TryGetProperty(camel, out var value) && !obj.TryGetProperty(snake, out value))) return null;
        long number;
        if (value.ValueKind == JsonValueKind.String)
        {
            if (!long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)) return null;
        }
        else if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out number)) return null;
        if (number <= 0) return null;
        try { return (milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number)).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
