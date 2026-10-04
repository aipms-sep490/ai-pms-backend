using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.UnitTests.Meetings;

public sealed class LiveKitWebhookVerifierTests
{
    private const string Secret = "test-livekit-secret-at-least-32-bytes-long";
    private const string Body = "{\"id\":\"EV_test\",\"event\":\"room_started\",\"room\":{\"name\":\"vm-test\"}}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);

    [Theory]
    [InlineData("")]
    [InlineData("Bearer ")]
    public void AcceptsSignedProviderFormatAndLegacyBearer(string prefix) =>
        Assert.True(Verifier().Verify(prefix + Token(), Body));

    [Theory]
    [InlineData("secret")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("missing-exp")]
    [InlineData("missing-hash")]
    [InlineData("hex-hash")]
    [InlineData("hash-case")]
    [InlineData("algorithm")]
    public void RejectsInvalidSignatureOrClaims(string invalid) =>
        Assert.False(Verifier().Verify(Token(invalid), Body));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    public void RejectsMissingOrMalformedHeader(string? token) => Assert.False(Verifier().Verify(token, Body));

    [Fact]
    public void RejectsBodyMutationEvenIfJsonSemanticsAreUnchanged() =>
        Assert.False(Verifier().Verify(Token(), Body + "\n"));

    [Fact]
    public void RejectsWhenVideoDisabled() => Assert.False(Verifier(false).Verify(Token(), Body));

    [Fact]
    public async Task AuthenticatesBeforeRecordingAndDeduplicatesProviderEvent()
    {
        await using var db = new AipmsDbContext(new DbContextOptionsBuilder<AipmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new VideoProviderEventService(db, Verifier(), new FixedClock());
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ProcessLiveKitAsync(Body, Token("secret"), default));
        Assert.Empty(db.VideoProviderEvents);
        await service.ProcessLiveKitAsync(Body, Token(), default);
        await service.ProcessLiveKitAsync(Body, Token(), default);
        var record = Assert.Single(db.VideoProviderEvents);
        Assert.Equal("EV_test", record.ProviderEventId);
        Assert.Equal("PROCESSED", record.ProcessingStatus);
        Assert.Null(record.MeetingVideoSessionId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Body))).ToLowerInvariant(), record.PayloadHash);
    }

    private static LiveKitWebhookVerifier Verifier(bool enabled = true) => new(Options.Create(new VideoMeetingOptions
    {
        Enabled = enabled, ApiKey = "test-api-key", ApiSecret = Secret
    }), new FixedClock());

    // Independently produce the wire format, without relying on the verifier's JWT library.
    private static string Token(string? invalid = null)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Body));
        var claims = new Dictionary<string, object>
        {
            ["iss"] = invalid == "issuer" ? "another-key" : "test-api-key",
            ["nbf"] = Now.ToUnixTimeSeconds() + (invalid == "future" ? 60 : -300),
            ["exp"] = Now.ToUnixTimeSeconds() + (invalid == "expired" ? -60 : 300),
            ["sha256"] = invalid == "hex-hash" ? Convert.ToHexString(hash).ToLowerInvariant()
                : invalid == "hash-case" ? Convert.ToBase64String(hash).ToLowerInvariant() : Convert.ToBase64String(hash)
        };
        if (invalid == "missing-exp") claims.Remove("exp");
        if (invalid == "missing-hash") claims.Remove("sha256");
        var algorithm = invalid == "algorithm" ? "HS384" : "HS256";
        var input = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = algorithm })) + "." + B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var key = Encoding.UTF8.GetBytes(invalid == "secret" ? Secret + "wrong" : Secret);
        var bytes = Encoding.UTF8.GetBytes(input);
        return input + "." + B64(algorithm == "HS384" ? HMACSHA384.HashData(key, bytes) : HMACSHA256.HashData(key, bytes));
    }

    private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
