using System.Net;

namespace AIPMS.IntegrationTests;

public sealed class FrontendCorsEndpointTests(AipmsWebApplicationFactory factory)
    : IClassFixture<AipmsWebApplicationFactory>
{
    [Theory]
    [InlineData("/api/v1/auth/login", "POST")]
    [InlineData("/api/v1/meetings/1/video/join", "POST")]
    [InlineData("/api/v1/meetings/1", "PUT")]
    public async Task Allowed_frontend_can_send_credentialed_requests(string path, string method)
    {
        using var client = factory.CreateClient();
        using var request = Preflight(path, method, "http://localhost:5173");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains(method, response.Headers.GetValues("Access-Control-Allow-Methods"));
    }

    [Theory]
    [InlineData("https://untrusted.example")]
    [InlineData("http://localhost:5173.untrusted.example")]
    [InlineData("null")]
    public async Task Unlisted_origin_receives_no_cors_grant(string origin)
    {
        using var client = factory.CreateClient();
        using var request = Preflight("/api/v1/auth/login", "POST", origin);
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Actual_response_includes_credentialed_cors_headers()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system");
        request.Headers.Add("Origin", "http://localhost:5173");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    private static HttpRequestMessage Preflight(string path, string method, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");
        return request;
    }
}
