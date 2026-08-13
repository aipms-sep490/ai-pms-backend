using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AIPMS.IntegrationTests;

public sealed class SystemEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SystemEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSystem_ReturnsApiStatus()
    {
        var response = await _client.GetAsync("/api/system");
        var body = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
    }
}
