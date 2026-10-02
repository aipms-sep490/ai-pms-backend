using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Video;

internal sealed class LiveKitVideoMeetingProvider(IHttpClientFactory clients, IOptions<VideoMeetingOptions> options) : IVideoMeetingProvider
{
    private readonly VideoMeetingOptions settings = options.Value;
    public string Provider => "LIVEKIT";

    public async Task CreateRoomAsync(VideoProviderRoomRequest request, CancellationToken cancellationToken)
    {
        await SendAsync("twirp/livekit.RoomService/CreateRoom", new { name = request.RoomKey, empty_timeout = 300 }, cancellationToken);
    }

    public async Task<VideoJoinCredentialDto> CreateJoinCredentialAsync(VideoProviderJoinRequest request, CancellationToken cancellationToken)
    {
        var token = CreateToken(request);
        return await Task.FromResult(new VideoJoinCredentialDto(Provider, settings.ServerUrl, request.ParticipantIdentity, request.ParticipantName, token, request.ExpiresAt, new VideoJoinCapabilities(true, true, true, request.Moderator)));
    }

    public async Task CloseRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        await SendAsync("twirp/livekit.RoomService/DeleteRoom", new { room = roomKey }, cancellationToken);
    }

    public async Task<VideoProviderRoomSnapshot?> GetRoomAsync(string roomKey, CancellationToken cancellationToken)
    {
        using var response = await SendAsync("twirp/livekit.RoomService/ListRooms", new { names = new[] { roomKey } }, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("rooms", out var rooms) || rooms.GetArrayLength() == 0) return null;
        var room = rooms[0];
        var count = room.TryGetProperty("numParticipants", out var value) && value.TryGetInt32(out var parsed) ? parsed : (int?)null;
        return new VideoProviderRoomSnapshot(roomKey, count);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (!settings.IsReady) throw new ServiceUnavailableException("Video provider is not configured.");
        var client = clients.CreateClient("livekit");
        var restBase = settings.ServerUrl.TrimEnd('/');
        if (restBase.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)) restBase = "https://" + restBase[6..];
        else if (restBase.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)) restBase = "http://" + restBase[5..];
        client.BaseAddress = new Uri(restBase + "/");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAdminToken());
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("VIDEO_PROVIDER_TIMEOUT", "VIDEO_PROVIDER_TIMEOUT");
        }
        using (response)
        {
            if (response.IsSuccessStatusCode) return new HttpResponseMessage(response.StatusCode) { Content = new StringContent(await response.Content.ReadAsStringAsync(cancellationToken)) };
            var status = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ? "VIDEO_PROVIDER_TIMEOUT" : "VIDEO_PROVIDER_UNAVAILABLE";
            throw new ServiceUnavailableException(status, status);
        }
    }

    private string CreateAdminToken() => CreateJwt(new Dictionary<string, object> { ["video"] = new Dictionary<string, object> { ["roomAdmin"] = true, ["roomCreate"] = true, ["roomList"] = true, ["roomRecord"] = false } });
    private string CreateToken(VideoProviderJoinRequest request) => CreateJwt(new Dictionary<string, object> { ["sub"] = request.ParticipantIdentity, ["name"] = request.ParticipantName, ["video"] = new Dictionary<string, object> { ["roomJoin"] = true, ["room"] = request.RoomKey, ["canPublish"] = true, ["canPublishData"] = true, ["canSubscribe"] = true, ["roomAdmin"] = request.Moderator } }, request.ExpiresAt);
    private string CreateJwt(Dictionary<string, object> claims, DateTime? expires = null)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = new Dictionary<string, object>(claims) { ["iss"] = settings.ApiKey, ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["exp"] = new DateTimeOffset(expires ?? DateTime.UtcNow.AddMinutes(5)).ToUnixTimeSeconds() };
        var encoded = header + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.ApiSecret));
        return encoded + "." + Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(encoded)));
    }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}


