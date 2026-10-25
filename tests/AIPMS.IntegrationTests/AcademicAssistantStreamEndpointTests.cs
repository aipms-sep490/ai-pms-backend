using System.Reflection;
using System.Text.Json;
using AIPMS.Application.Features.AcademicKnowledge;
using AIPMS.Application.Features.AcademicKnowledge.Abstractions;
using AIPMS.Application.Abstractions.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests;

public sealed class AcademicAssistantStreamEndpointTests
{
    [Fact]
    public async Task HttpStreamFlushesMultipleFramesAndFinalResponseWithoutMutatingStartedHeaders()
    {
        await using var app = new StreamingFactory();
        using var client = app.CreateAuthenticatedClient();
        using var form = new MultipartFormDataContent { { new StringContent("Hello"), "query" } };
        using var response = await client.PostAsync("/api/v1/academic/assistant/stream", form);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("preview", JsonDocument.Parse(lines[0]).RootElement.GetProperty("type").GetString());
        Assert.Equal("completed", JsonDocument.Parse(lines[2]).RootElement.GetProperty("type").GetString());
    }

    private sealed class StreamingFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => {
                services.RemoveAll<IAcademicKnowledgeService>();
                services.AddScoped<IAcademicKnowledgeService>(sp => {
                    var service = DispatchProxy.Create<IAcademicKnowledgeService, StreamingStub>();
                    ((StreamingStub)(object)service).RequestContext = sp.GetRequiredService<IRequestContext>();
                    return service;
                });
            });
        }
    }

    public class StreamingStub : DispatchProxy
    {
        public IRequestContext RequestContext { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IAcademicKnowledgeService.AskStreamingAsync)) throw new NotSupportedException();
            return Generate((Func<AssistantStreamEvent, Task>)args![2]!);
        }
        private async Task<AcademicAssistantResponseDto> Generate(Func<AssistantStreamEvent, Task> emit)
        {
            await emit(new("preview", "Hello"));
            await emit(new("preview", "Hello world"));
            Assert.NotEqual(Guid.Empty, RequestContext.CorrelationId);
            return new("Hello world", "GENERAL", [], [], false, null, "COMPLETED", DateTime.UtcNow);
        }
    }
}
