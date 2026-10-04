using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Video;

internal sealed class LiveKitVideoMeetingProvider(IHttpClientFactory clients, IOptions<VideoMeetingOptions> options, TimeProvider clock) : IVideoMeetingProvider
{
    private readonly VideoMeetingOptions settings = options.Value;
    public string Provider => "LIVEKIT";

    public async Task CreateRoomAsync(VideoProviderRoomRequest request, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("CreateRoom", new { name = request.RoomKey, empty_timeout = 300 }, request.RoomKey, cancellationToken);
    }

    public Task<VideoJoinCredentialDto> CreateJoinCredentialAsync(VideoProviderJoinRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReady();
        var now = clock.GetUtcNow().UtcDateTime;
        if (request.ExpiresAt <= now || request.ExpiresAt > now.AddSeconds(300)) throw new ArgumentException("Join token must expire within five minutes.");
        // Moderation is an AI-PMS capability. A media token must never authorize the RoomService API.
        var token = CreateJwt(new() { ["sub"] = request.ParticipantIdentity, ["name"] = request.ParticipantName,
            ["video"] = new { roomJoin = true, room = request.RoomKey, canPublish = true, canPublishData = true, canSubscribe = true } }, request.ExpiresAt);
        return Task.FromResult(new VideoJoinCredentialDto(Provider, SocketUrl(), request.ParticipantIdentity, request.ParticipantName,
            token, request.ExpiresAt, new(true, true, true, request.Moderator)));
    }

    public async Task CloseRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("DeleteRoom", new { room = roomKey }, roomKey, cancellationToken);
    }

    public async Task<VideoProviderRoomSnapshot?> GetRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("ListRooms", new { names = new[] { roomKey } }, roomKey, cancellationToken);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!doc.RootElement.TryGetProperty("rooms", out var rooms) || rooms.GetArrayLength() == 0) return null;
        var room = rooms[0];
        int? count = (room.TryGetProperty("numParticipants", out var value) || room.TryGetProperty("num_participants", out value))
            && value.TryGetInt32(out var parsed) ? parsed : null;
        return new(roomKey, count);
    }

    private async Task<HttpResponseMessage> SendAsync(string method, object body, string room, CancellationToken ct)
    {
        EnsureReady();
        var url = new UriBuilder(settings.ServerUrl) { Scheme = "https", Port = -1, Path = "/twirp/livekit.RoomService/" + method };
        using var request = new HttpRequestMessage(HttpMethod.Post, url.Uri);
        var grant = method switch
        {
            // LiveKit requires roomCreate for both creating and deleting rooms.
            "CreateRoom" or "DeleteRoom" => new Dictionary<string, object> { ["roomCreate"] = true },
            "ListRooms" => new Dictionary<string, object> { ["roomList"] = true },
            _ => new Dictionary<string, object> { ["roomAdmin"] = true, ["room"] = room }
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(new() { ["video"] = grant }, clock.GetUtcNow().UtcDateTime.AddMinutes(1)));
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            var response = await clients.CreateClient("livekit").SendAsync(request, ct);
            if (response.IsSuccessStatusCode || (method == "DeleteRoom" && response.StatusCode == HttpStatusCode.NotFound)) return response;
            var status = response.StatusCode;
            response.Dispose();
            if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new VideoProviderPermanentException();
            var code = status is HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout ? "VIDEO_PROVIDER_TIMEOUT" : "VIDEO_PROVIDER_UNAVAILABLE";
            throw new ServiceUnavailableException("The video provider is temporarily unavailable.", code);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServiceUnavailableException("The video provider timed out.", "VIDEO_PROVIDER_TIMEOUT"); }
        catch (HttpRequestException)
        { throw new ServiceUnavailableException("The video provider is temporarily unavailable.", "VIDEO_PROVIDER_UNAVAILABLE"); }
    }

    private void EnsureReady()
    {
        if (!settings.Enabled || !settings.IsReady) throw new ServiceUnavailableException("Video is disabled or not configured.", "VIDEO_NOT_ENABLED");
    }
    private string SocketUrl() => new UriBuilder(settings.ServerUrl) { Scheme = "wss", Port = -1 }.Uri.ToString().TrimEnd('/');
    private string CreateJwt(Dictionary<string, object> claims, DateTime expires)
    {
        claims["iss"] = settings.ApiKey;
        claims["iat"] = clock.GetUtcNow().ToUnixTimeSeconds();
        claims["nbf"] = clock.GetUtcNow().ToUnixTimeSeconds();
        claims["exp"] = new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var data = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" })) + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        return data + "." + Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiSecret), Encoding.UTF8.GetBytes(data)));
    }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class VideoProviderPermanentException : Exception;
