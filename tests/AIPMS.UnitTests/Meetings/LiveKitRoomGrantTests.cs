using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Video;
using Microsoft.Extensions.Options;

namespace AIPMS.UnitTests.Meetings;

public sealed class LiveKitRoomGrantTests
{
    [Fact]
    public async Task CloseRoomUsesServerOnlyRoomCreateGrantRequiredByLiveKit()
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        var provider = new LiveKitVideoMeetingProvider(new ClientFactory(client), Options.Create(new VideoMeetingOptions
        {
            Enabled = true, ServerUrl = "wss://test.livekit.cloud", ApiKey = "test-key",
            ApiSecret = "test-secret-long-enough-for-hmac-sha256"
        }), TimeProvider.System);
        await provider.CloseRoomAsync("vm-test", default);
        Assert.Equal("/twirp/livekit.RoomService/DeleteRoom", handler.Path);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(handler.Token);
        using var grant = JsonDocument.Parse(JsonSerializer.Serialize(jwt.Payload["video"]));
        Assert.True(grant.RootElement.GetProperty("roomCreate").GetBoolean());
        Assert.False(grant.RootElement.TryGetProperty("roomAdmin", out _));
        Assert.False(grant.RootElement.TryGetProperty("roomJoin", out _));
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("vm-test", body.RootElement.GetProperty("room").GetString());
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Token { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri!.AbsolutePath;
            Token = request.Headers.Authorization!.Parameter;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
