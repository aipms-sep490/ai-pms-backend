using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIPMS.AI.Configuration;
using AIPMS.AI.Providers;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class OpenAiTextGenerationProviderTests
{
    [Fact]
    public async Task StreamingReadsActualDeltasAndRequiresCompletionMarker()
    {
        using var handler = new StubHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"Hello \"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        });
        using var client = new HttpClient(handler);
        var chunks = new List<string>();
        var result = await Create(client).GenerateStreamingAsync("System", "Question", [], value => { chunks.Add(value); return Task.CompletedTask; });
        Assert.Equal(new[] { "Hello ", "world" }, chunks);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public async Task TruncatedProviderStreamIsNotAccepted()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n") }));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<System.IO.IOException>(() => Create(client).GenerateStreamingAsync("System", "Question", [], _ => Task.CompletedTask));
    }

    [Fact]
    public async Task RouterCanFinishWithStopWithoutDoneSentinel()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n") }));
        using var client = new HttpClient(handler);
        Assert.Equal("Hello", await Create(client).GenerateStreamingAsync("System", "Question", [], _ => Task.CompletedTask));
    }
    [Fact]
    public async Task UsesConfiguredRouterEndpointModelAndBearerToken()
    {
        using var handler = new StubHandler(async (request, ct) =>
        {
            Assert.Equal("http://router.test/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-only-key", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("cx/test-model", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"Verified answer\"}}]}", Encoding.UTF8, "application/json")
            };
        });
        using var client = new HttpClient(handler);
        Assert.Equal("Verified answer", await Create(client).GenerateTextAsync("Use evidence only", "Question"));
    }

    [Fact]
    public async Task SendsImagesAsMultimodalContentInsteadOfText()
    {
        using var handler = new StubHandler(async (request, ct) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var parts = body.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert.Equal("text", parts[0].GetProperty("type").GetString());
            Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
            Assert.Equal("data:image/png;base64,AQID", parts[1].GetProperty("image_url").GetProperty("url").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"message":{"content":"Image answer"}}]}""") };
        });
        using var client = new HttpClient(handler);
        Assert.Equal("Image answer", await Create(client).GenerateWithImagesAsync("System", "Read image", [new("image/png", [1, 2, 3])]));
    }

    [Fact]
    public async Task UnauthorizedRouterDoesNotReturnAnAnswerOrExposeResponseBody()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("private-provider-details")
        }));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Create(client).GenerateTextAsync("System", "Query"));
        Assert.DoesNotContain("private-provider-details", error.Message);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(client).GenerateTextAsync("System", "Query", cancellation.Token));
    }

    private static OpenAiTextGenerationProvider Create(HttpClient client) => new(client, Options.Create(new AiAssistantOptions
    {
        OpenAi = new OpenAiOptions
        {
            Endpoint = "http://router.test/v1/chat/completions",
            Model = "cx/test-model",
            ApiKey = "test-only-key"
        }
    }));

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
