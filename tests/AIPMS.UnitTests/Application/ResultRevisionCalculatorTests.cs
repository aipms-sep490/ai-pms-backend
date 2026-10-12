using System.Text.Json;
using System.Text.Json.Nodes;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Infrastructure.Services.Projects;

namespace AIPMS.UnitTests.Application;

public sealed class ResultRevisionCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
    private static string Snapshot(long? student = null, string version = "DECIMAL_V2", decimal score = 1, decimal max = 3)
    {
        SchemeComponentDto component = new(1, "Common", "COMMON", null, 5, 100, 100, 1, null, "DEFENSE");
        SchemePolicyInput policy = new(false, "APPROVED", "SYNTHETIC-ONLY", CalculationVersion: version);
        var scheme = new EvaluationSchemeDto(10, 10, 1, 20, 30, "Frozen scheme", "PUBLISHED", 5, "scheme-token", 40,
            [component], [new(101, 201, 301), new(102, 201, 301)],
            version == "LEGACY_V1" ? EvaluationSchemeRules.CalculationRule : version, policy, Now);
        EvaluationScoreRecord[] scores = [new(11, "Criterion", null, 100, max, 0, true, score, "private")];
        var total = VersionedResultCalculator.EvaluationTotal(scores, version);
        var evaluation = new EvaluationDraftRecord(50, 60, 20, 70, 5, "Rubric", 5, 1, "LECTURER", "FINALIZED", "private",
            EvaluationScoring.FinalTotal(scores), "evaluation-token", Now, Now, scores,
            new(70, Now, new(80, 81, Now, 1, 1, [90], "DEFENSE")));
        var contribution = new ResultContributionDto(60, 50, 70, 5, 100, total, evaluation.ConcurrencyToken);
        var breakdown = new ResultBreakdownDto(scheme.Name, 1, version, false, false, "APPROVED", "READY", total, total, null, [],
            [new(1, "Common", "COMMON", null, 100, total, 1, 1, "DEFENSE")]);
        return JsonSerializer.Serialize(new { SnapshotVersion = 2, Breakdown = breakdown, Scheme = scheme,
            Target = student.HasValue ? scheme.Students.Single(s => s.StudentId == student) : null,
            Policy = new { Id = 40, Status = "LOCKED" }, PackageId = 80, Contributions = new[] { contribution },
            Evaluations = new[] { new { Id = 60, ScopeSnapshotJson = JsonSerializer.Serialize(new {
                Scheme = scheme, component, StudentId = (long?)null, FinalSubmissionId = 80 }), evaluation } } });
    }

    [Theory]
    [InlineData("LEGACY_V1")]
    [InlineData("DECIMAL_V2")]
    public void Amendment_uses_frozen_calculator_and_preserves_evidence_and_prior_json(string version)
    {
        var original = Snapshot(version: version);
        var revised = ResultRevisionCalculator.Calculate(original, [new(50, 11, 2, "Transcription")]);
        Assert.Equal(6.67m, revised.TotalScore);
        Assert.Equal(version == "LEGACY_V1" ? 6.67m : 2m / 3m * 100m / 10m, revised.Breakdown.UnroundedTotal);
        Assert.Equal(3.33m, ResultRevisionCalculator.Calculate(original, []).TotalScore);
        var before = JsonNode.Parse(original)!["Evaluations"]![0]!["evaluation"]!["Finalization"]!.ToJsonString();
        var after = JsonNode.Parse(revised.FrozenInputs)!["Evaluations"]![0]!["evaluation"]!["Finalization"]!.ToJsonString();
        Assert.Equal(before, after);
        Assert.Equal(0m, ResultRevisionCalculator.Calculate(revised.FrozenInputs, [new(50, 11, 0, "Confirmed zero")]).TotalScore);
    }

    [Theory]
    [InlineData("score")]
    [InlineData("weight")]
    [InlineData("package")]
    [InlineData("scope")]
    [InlineData("demo")]
    [InlineData("total")]
    [InlineData("missing")]
    public void Inconsistent_or_incomplete_provenance_fails_closed(string damage)
    {
        var root = JsonNode.Parse(Snapshot())!;
        switch (damage)
        {
            case "score": root["Contributions"]![0]!["Score"] = 9; break;
            case "weight": root["Contributions"]![0]!["WeightPercent"] = 50; break;
            case "package": root["PackageId"] = 999; break;
            case "scope": root["Scheme"]!["Components"]![0]!["Scope"] = "INDIVIDUAL"; break;
            case "demo": root["Scheme"]!["Policy"]!["IsDemo"] = true; break;
            case "total": root["Breakdown"]!["UnroundedTotal"] = 9; break;
            case "missing": root["Evaluations"]![0]!["evaluation"]!["Scores"]![0]!["Score"] = null; break;
        }
        Assert.Throws<ConflictException>(() => ResultRevisionCalculator.Calculate(root.ToJsonString(), [new(50, 11, 2, "Correction")]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not-json")]
    public void Unknown_legacy_input_cannot_be_reconstructed(string json) =>
        Assert.Throws<ConflictException>(() => ResultRevisionCalculator.Calculate(json, [new(50, 11, 2, "Correction")]));

    [Fact]
    public void Common_amendment_requires_all_affected_publications_and_recalculates_every_target()
    {
        ResultRevisionStore.Target Target(long key) => new(key, 1, null, Snapshot(key == 0 ? null : key), "{\"TotalScore\":3.33}");
        ScoreAmendmentInput[] edits = [new(50, 11, 2, "Correction")];
        Assert.Throws<ConflictException>(() => ResultRevisionStore.Calculate([Target(0), Target(101)], edits));
        var result = ResultRevisionStore.Calculate([Target(0), Target(101), Target(102)], edits);
        Assert.Equal(new long[] { 0, 101, 102 }, result.Select(t => t.Current.Key));
        Assert.All(result, t => Assert.Equal(6.67m, t.Calculation.TotalScore));
    }

    [Fact]
    public void Affected_results_must_agree_on_original_inputs()
    {
        var targets = new[] {
            new ResultRevisionStore.Target(0, 1, null, Snapshot(), "{\"TotalScore\":3.33}"),
            new ResultRevisionStore.Target(101, 1, null, Snapshot(101, score: 2), "{\"TotalScore\":6.67}"),
            new ResultRevisionStore.Target(102, 1, null, Snapshot(102), "{\"TotalScore\":3.33}") };
        Assert.Throws<ConflictException>(() => ResultRevisionStore.Calculate(targets, [new(50, 11, 0, "Correction")]));
    }
}
