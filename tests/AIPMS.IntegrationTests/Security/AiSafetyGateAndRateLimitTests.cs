using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.ProgressReports.Commands;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests.Security;

[Trait("Category", "InMemory")]
public sealed class AiSafetyGateAndRateLimitTests
{
    private sealed class AiGateFactory(Dictionary<string, string?> overrides) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(overrides));
        }
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) return c.GetString();
        if (doc.RootElement.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String) return ec.GetString();
        return null;
    }

    [Fact]
    public async Task AiSuiteMeetingInsightsChatAiAndLegacyAsk_WhenMasterFlagDisabled_Return503AiFeatureDisabled()
    {
        using var app = new AiGateFactory(new Dictionary<string, string?>
        {
            ["AiAssistant:Enabled"] = "false",
            ["Chat:Enabled"] = "true"
        });
        using var client = app.CreateAuthenticatedClient(1001, roles: [AppRoles.DepartmentStaff]);

        var endpoints = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/ai-suite/projects/1/submissions/preflight", new { }),
            (HttpMethod.Post, "/api/v1/meetings/1/video/ai/summarize", new { insightType = "SUMMARY" }),
            (HttpMethod.Post, "/api/v1/ai/insights/progress", new AnalyzeProgressCommand(10, 4, 2, 0.4m)),
            (HttpMethod.Post, "/api/v1/chat/conversations/1/ai/summary", new { messageCount = 20 }),
            (HttpMethod.Post, "/api/v1/chat/conversations/1/ai/assist", new { mode = "QA", prompt = "test" }),
            (HttpMethod.Post, "/api/v1/projects/1/ai/ask", new { query = "Tiến độ thế nào?" })
        };

        foreach (var (method, url, body) in endpoints)
        {
            using var req = new HttpRequestMessage(method, url)
            {
                Content = body is null ? null : JsonContent.Create(body)
            };
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
            Assert.Equal(WorkflowErrorCodes.AiFeatureDisabled, await ReadProblemCode(res));
        }
    }

    [Fact]
    public async Task DisabledHighRiskAiEndpoints_Return503_EvenWhenMasterAndSuiteEnabled()
    {
        using var app = new AiGateFactory(new Dictionary<string, string?>
        {
            ["AiAssistant:Enabled"] = "true",
            ["AiAssistant:SuiteEnabled"] = "true",
            ["AiAssistant:DailyQuotaPerUser"] = "0"
        });
        using var client = app.CreateAuthenticatedClient(1002, roles: [AppRoles.DepartmentStaff]);

        var disabledRoutes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/ai-suite/evaluations/assignments/1/rubric-copilot", null),
            (HttpMethod.Post, "/api/v1/ai-suite/appeals/1/review-assist", null),
            (HttpMethod.Post, "/api/v1/ai-suite/projects/1/contributions/fairness", null),
            (HttpMethod.Get, "/api/v1/ai-suite/projects/1/contributions/fairness/latest", null),
            (HttpMethod.Post, "/api/v1/ai-suite/projects/1/evaluation-schemes/suggest", null),
            (HttpMethod.Post, "/api/v1/projects/1/ai/ask", new { query = "Legacy project ask" })
        };

        foreach (var (method, url, body) in disabledRoutes)
        {
            using var req = new HttpRequestMessage(method, url)
            {
                Content = body is null ? null : JsonContent.Create(body)
            };
            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
            Assert.Equal(WorkflowErrorCodes.AiFeatureDisabled, await ReadProblemCode(res));
        }
    }

    [Fact]
    public async Task AiEndpoints_WhenEnabledWithinQuota_Proceed_AndExceedingDailyQuota_Returns429WithRetryAfter()
    {
        using var app = new AiGateFactory(new Dictionary<string, string?>
        {
            ["AiAssistant:Enabled"] = "true",
            ["AiAssistant:InsightsEnabled"] = "true",
            ["AiAssistant:DailyQuotaPerUser"] = "2"
        });
        using var client = app.CreateAuthenticatedClient(9901, roles: [AppRoles.Student]);
        var cmd = new AnalyzeProgressCommand(10, 5, 0, 0.2m);

        var r1 = await client.PostAsJsonAsync("/api/v1/ai/insights/progress", cmd);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r2 = await client.PostAsJsonAsync("/api/v1/ai/insights/progress", cmd);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var r3 = await client.PostAsJsonAsync("/api/v1/ai/insights/progress", cmd);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.Equal(WorkflowErrorCodes.AiDailyQuotaExceeded, await ReadProblemCode(r3));
        Assert.True(r3.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task AiEndpoints_WhenPerMinuteRateLimitExceeded_Returns429RateLimited()
    {
        using var app = new AiGateFactory(new Dictionary<string, string?>
        {
            ["AiAssistant:Enabled"] = "true",
            ["AiAssistant:InsightsEnabled"] = "true",
            ["AiAssistant:DailyQuotaPerUser"] = "0"
        });
        using var client = app.CreateAuthenticatedClient(9902, roles: [AppRoles.Student]);
        var cmd = new AnalyzeProgressCommand(10, 5, 0, 0.2m);

        HttpResponseMessage? throttled = null;
        for (var i = 0; i < 22; i++)
        {
            var res = await client.PostAsJsonAsync("/api/v1/ai/insights/progress", cmd);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = res;
                break;
            }
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        Assert.NotNull(throttled);
        Assert.Equal(WorkflowErrorCodes.RateLimited, await ReadProblemCode(throttled!));
    }
}
