using System.Text.Json;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using Microsoft.Extensions.Configuration;
using AIPMS.Infrastructure.Services.Projects;

namespace AIPMS.UnitTests.AI;

public sealed class DeliverableReviewAnalyzerTests
{
    private static readonly ReviewCriterionSource[] Source = [new(1, null, "Architecture", "Deployment evidence")];
    private const string Report = "The service runs in two containers with a SQL database.";
    private const string Valid = """
        {"criteria":[{"criterionId":1,"status":"PASS","quotes":["The service runs in two containers"],"findings":["Deployment described."],"missingEvidence":[],"suggestions":[]}]}
        """;
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["AiAssistant:TimeoutSeconds"] = "1" }).Build();

    [Fact]
    public async Task Valid_provider_output_uses_backend_names_and_exact_quotes()
    {
        var result = await DeliverableReviewAnalyzer.Analyze(new Provider(Valid), Config, Source, Report, "Deployment", ["Technical report"], default);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal("Architecture", result.Criteria[0].CriterionName);
        Assert.Equal("PASS", result.Criteria[0].Status);
        Assert.Contains(result.Criteria[0].Findings, f => f.Contains("two containers"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"criteria\":[]}")]
    [InlineData("{\"criteria\":[{\"criterionId\":999}]}")]
    public async Task Invalid_provider_output_falls_back(string output)
    {
        var result = await DeliverableReviewAnalyzer.Analyze(new Provider(output), Config, Source, Report, null, [], default);
        Assert.Equal("FALLBACK", result.Status);
        Assert.DoesNotContain(result.Criteria, c => c.Status == "PASS");
    }

    [Fact]
    public async Task Provider_timeout_falls_back_but_request_cancellation_propagates()
    {
        var provider = new Provider(null);
        Assert.Equal("FALLBACK", (await DeliverableReviewAnalyzer.Analyze(provider, Config, Source, Report, null, [], default)).Status);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeliverableReviewAnalyzer.Analyze(provider, Config, Source, Report, null, [], cancellation.Token));
    }

    [Fact]
    public void Readiness_counts_leaf_criteria_once()
    {
        ReviewCriterionSource[] sources = [new(1, null, "Parent", null), new(2, 1, "Child", null), new(3, 1, "Child two", null)];
        Assert.Equal(50, DeliverableReviewAnalyzer.ReadinessPercent(sources, [new(1, "Parent", "PASS", [], [], []), new(2, "Child", "PASS", [], [], []), new(3, "Child two", "MISSING", [], [], [])]));
    }

    private sealed class Provider(string? output) : IAiTextGenerationProvider
    {
        public string ModelVersion => "TEST_MODEL";
        public async Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            if (output != null) return output;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "";
        }
    }

    [Fact]
    public void Fallback_marks_only_named_criteria_as_partial_and_never_as_official_pass()
    {
        var criteria = new[] { new ReviewCriterionSource(1, null, "Kiến trúc", "Sơ đồ"), new ReviewCriterionSource(2, null, "Kiểm thử", "Minh chứng") };
        var result = DeliverableReviewAnalyzer.Fallback(criteria, "Phần kiến trúc của hệ thống.");
        Assert.Equal("FALLBACK", result.Status);
        Assert.Equal("PARTIAL", result.Criteria[0].Status);
        Assert.Equal("MISSING", result.Criteria[1].Status);
        Assert.Contains("không đánh giá chất lượng", result.Limitation);
    }

    [Fact]
    public void Invalid_model_output_is_rejected_when_quote_is_not_in_report()
    {
        var criteria = new[] { new ReviewCriterionSource(1, null, "Kiến trúc", "Sơ đồ") };
        var raw = "{\"criteria\":[{\"criterionId\":1,\"status\":\"PASS\",\"quotes\":[\"not in report\"],\"findings\":[\"ok\"],\"missingEvidence\":[],\"suggestions\":[]}] }";
        Assert.Throws<JsonException>(() => DeliverableReviewAnalyzer.Parse(raw, criteria, "Nội dung khác."));
    }
}
