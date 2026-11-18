using System.Text.Json;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Queries;
using AIPMS.Application.Features.Topics.Validators;
using AIPMS.Infrastructure.Services.Topics;
using Microsoft.Extensions.Configuration;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class TopicSimilarityAnalyzerTests
{
    private static readonly TopicAiSimilarityRequest Input = new(4, "Nền tảng bán hàng cho tiệm nhỏ",
        "Cửa hàng địa phương cần quản lý đơn hàng và tồn kho.", "Giúp chủ tiệm theo dõi hoạt động bán hàng.",
        "Website quản lý sản phẩm, đơn hàng và tồn kho.", "Thương mại điện tử", ["cửa hàng", "bán hàng"]);
    private static readonly TopicSimilarityCandidate Candidate = new(8, "TOPIC", "Giải pháp thương mại cho cửa hàng địa phương",
        "FA26", "Cửa hàng địa phương cần quản lý đơn hàng và tồn kho.", "Theo dõi sản phẩm và đơn hàng.",
        "Website bán hàng và báo cáo doanh thu.", "E-commerce", ["local commerce"]);
    private static IConfiguration Config(int seconds = 20) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["AiAssistant:TimeoutSeconds"] = seconds.ToString() }).Build();
    private static string Response(int key = 1, string? quote = null, string level = "HIGH") => JsonSerializer.Serialize(new {
        matches = new[] { new { key, level, reason = "Cùng giải quyết bán hàng và tồn kho cho cửa hàng địa phương.",
            sharedAspects = new[] { "Đơn hàng và tồn kho" }, differences = new[] { "Chưa thấy mục tiêu Marketing ở nguồn đối chiếu" },
            candidateExcerpt = quote ?? "Cửa hàng địa phương cần quản lý đơn hàng và tồn kho.",
            proposalExcerpt = "Website quản lý sản phẩm, đơn hàng và tồn kho." } } });

    [Fact]
    public async Task Paraphrased_titles_return_only_server_owned_identity_and_verbatim_evidence()
    {
        var provider = new Provider((_, _, _) => Task.FromResult(Response()));
        var result = await TopicSimilarityAnalyzer.Analyze(provider, Config(), Input, [Candidate], false, default);
        Assert.Equal("COMPLETED", result.Status);
        var match = Assert.Single(result.Matches);
        Assert.Equal(Candidate.Title, match.Title);
        Assert.Equal(Candidate.Id, match.Id);
        Assert.Equal("HIGH", match.SimilarityLevel);
        Assert.Contains(match.CandidateExcerpt, TopicSimilarityAnalyzer.Evidence(Candidate));
        Assert.Contains(match.ProposalExcerpt, TopicSimilarityAnalyzer.Proposal(Input));
        Assert.Contains("không xác nhận đạo văn", result.Limitation);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(123456)]
    public async Task Invented_candidate_identity_causes_honest_fallback(int key)
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => Task.FromResult(Response(key))),
            Config(), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", result.Status);
        Assert.Empty(result.Matches);
        Assert.Equal(0, result.ReviewedCount);
        Assert.Contains("Chưa thể kết luận", result.Limitation);
    }

    [Theory]
    [InlineData("bịa ra một trích đoạn không có nguồn")]
    [InlineData("Cửa hàng...")]
    [InlineData("short")]
    public async Task Ungrounded_or_truncated_quotes_are_not_shown_as_AI_findings(string quote)
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => Task.FromResult(Response(quote: quote))),
            Config(), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", result.Status);
        Assert.Empty(result.Matches);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"matches\":null}")]
    [InlineData("{}")]
    public async Task Malformed_response_never_becomes_a_uniqueness_claim(string raw)
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => Task.FromResult(raw)),
            Config(), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", result.Status);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task No_candidates_or_local_provider_do_not_spend_a_model_call()
    {
        var provider = new Provider((_, _, _) => throw new InvalidOperationException());
        var noData = await TopicSimilarityAnalyzer.Analyze(provider, Config(), Input, [], false, default);
        Assert.Equal("INSUFFICIENT_DATA", noData.Status);
        Assert.Equal(0, provider.Calls);
        var local = new Provider((_, _, _) => throw new InvalidOperationException(), "GROUNDED_LOCAL_V1");
        var fallback = await TopicSimilarityAnalyzer.Analyze(local, Config(), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", fallback.Status);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task No_matches_limits_the_claim_to_selected_sources_and_reports_missing_proposal_fields()
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => Task.FromResult("{\"matches\":[]}")),
            Config(), Input with { ProblemStatement = null }, [Candidate], true, default);
        Assert.Equal("COMPLETED", result.Status);
        Assert.Equal(1, result.ReviewedCount);
        Assert.Empty(result.Matches);
        Assert.Contains("trong tập đã phân tích", result.Summary);
        Assert.Contains("giới hạn", result.Limitation);
        Assert.Contains("Bổ sung vấn đề", result.Limitation);
    }

    [Fact]
    public async Task Provider_error_is_fallback_but_request_cancellation_propagates()
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => throw new IOException()),
            Config(), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", result.Status);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TopicSimilarityAnalyzer.Analyze(
            new Provider((_, _, _) => Task.FromResult(Response())), Config(), Input, [Candidate], false, cancelled.Token));
    }

    [Fact]
    public async Task Slow_provider_is_cancelled_within_its_budget_and_does_not_claim_success()
    {
        var result = await TopicSimilarityAnalyzer.Analyze(new Provider(async (_, _, ct) => {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Response();
        }), Config(1), Input, [Candidate], false, default);
        Assert.Equal("FALLBACK", result.Status);
    }

    [Fact]
    public async Task Candidate_selection_is_bounded_and_keeps_recent_candidates_without_keyword_overlap()
    {
        var recent = Candidate with { Id = 1, Title = "Retail assistance", ProblemStatement = "Independent recent source", Objectives = null,
            ExpectedOutput = null, Domain = null, Keywords = [] };
        var candidates = new[] { recent }.Concat(Enumerable.Range(2, 40).Select(id => Candidate with { Id = id })).ToArray();
        var selected = TopicSimilarityAnalyzer.Select(Input, candidates);
        Assert.InRange(selected.Length, 1, TopicSimilarityAnalyzer.MaxCandidates);
        Assert.Contains(recent, selected);
        var provider = new Provider((system, payload, _) => {
            Assert.Contains("untrusted", system);
            using var doc = JsonDocument.Parse(payload);
            Assert.InRange(doc.RootElement.GetProperty("candidates").GetArrayLength(), 1, 12);
            return Task.FromResult("{\"matches\":[]}");
        });
        var result = await TopicSimilarityAnalyzer.Analyze(provider, Config(), Input, candidates, false, default);
        Assert.Contains("giới hạn", result.Limitation);
        Assert.Equal(selected.Length, result.ReviewedCount);
    }

    [Fact]
    public async Task Duplicate_keys_or_invalid_levels_cannot_escape_validation()
    {
        var duplicate = JsonSerializer.Serialize(new { matches = new[] {
            JsonDocument.Parse(Response()).RootElement.GetProperty("matches")[0].Clone(),
            JsonDocument.Parse(Response()).RootElement.GetProperty("matches")[0].Clone() } });
        foreach (var response in new[] { duplicate, Response(level: "CERTAIN_PLAGIARISM") })
        {
            var result = await TopicSimilarityAnalyzer.Analyze(new Provider((_, _, _) => Task.FromResult(response)),
                Config(), Input, [Candidate], false, default);
            Assert.Equal("FALLBACK", result.Status);
        }
    }

    [Fact]
    public void Request_bounds_prevent_unbounded_prompts_and_arbitrary_project_identifiers()
    {
        var validator = new CheckTopicAiSimilarityQueryValidator();
        Assert.True(validator.Validate(new CheckTopicAiSimilarityQuery(Input)).IsValid);
        Assert.False(validator.Validate(new CheckTopicAiSimilarityQuery(Input with { TeamId = 0 })).IsValid);
        Assert.False(validator.Validate(new CheckTopicAiSimilarityQuery(Input with { ProjectId = -1 })).IsValid);
        Assert.False(validator.Validate(new CheckTopicAiSimilarityQuery(Input with { Title = new string('a', 301) })).IsValid);
        Assert.False(validator.Validate(new CheckTopicAiSimilarityQuery(Input with { Objectives = new string('a', 4001) })).IsValid);
        Assert.False(validator.Validate(new CheckTopicAiSimilarityQuery(Input with { Keywords = Enumerable.Repeat("a", 21).ToArray() })).IsValid);
    }

    private sealed class Provider(Func<string, string, CancellationToken, Task<string>> generate, string model = "TEST_SEMANTIC") : IAiTextGenerationProvider
    {
        public int Calls { get; private set; }
        public string ModelVersion => model;
        public Task<string> GenerateTextAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            return generate(systemPrompt, userPrompt, cancellationToken);
        }
    }
}
