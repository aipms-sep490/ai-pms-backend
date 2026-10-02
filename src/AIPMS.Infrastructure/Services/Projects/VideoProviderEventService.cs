using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using AsyncTask = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class VideoProviderEventService(AipmsDbContext db, IOptions<VideoMeetingOptions> options, TimeProvider clock) : IVideoProviderEventService
{
    private readonly VideoMeetingOptions settings = options.Value;
    public async AsyncTask ProcessLiveKitAsync(string rawBody, string? authorization, CancellationToken ct)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !Verify(authorization[7..], rawBody)) throw new UnauthorizedException("Invalid provider signature.");
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

    private bool Verify(string token, string rawBody)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler(); var jwt = handler.ReadJwtToken(token);
            if (jwt.Header.Alg is not "HS256" || jwt.Issuer != settings.ApiKey || jwt.ValidTo <= DateTime.UtcNow) return false;
            var separator = jwt.RawData.LastIndexOf('.'); if (separator <= 0) return false;
            var signing = jwt.RawData[..separator]; var signature = jwt.RawData[(separator + 1)..];
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.ApiSecret));
            var expected = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature))) return false;
            var expectedBodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
            return jwt.Payload.TryGetValue("sha256", out var bodyHash) && string.Equals(bodyHash?.ToString(), expectedBodyHash, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    private static string? ReadRoom(JsonElement root) => root.TryGetProperty("room", out var room) && room.TryGetProperty("name", out var name) ? name.GetString() : null;
    private static string? ReadParticipant(JsonElement root) => root.TryGetProperty("participant", out var p) && p.TryGetProperty("identity", out var id) ? id.GetString() : null;
    private static string? ReadConnection(JsonElement root) => root.TryGetProperty("participant", out var p) && p.TryGetProperty("sid", out var id) ? id.GetString() : null;
}


