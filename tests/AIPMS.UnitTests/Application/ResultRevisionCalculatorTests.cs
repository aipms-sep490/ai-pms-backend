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

    [Fact]
    public void PolV4_cold_evaluation_with_empty_deliverable_ids_and_zero_required_pol03_recalculates_cleanly()
    {
        SchemeComponentDto coldComp = new(1, "Cold", "COMMON", null, 5, 37.5m, 30m, 1, null, "COLD", "PROJECT");
        SchemeComponentDto defComp = new(2, "Defense", "COMMON", null, 5, 62.5m, 50m, 1, null, "DEFENSE", "PROJECT");
        SchemeComponentDto indComp = new(3, "GVHD 20%", "INDIVIDUAL", 201, null, 0m, 20m, 0, null, "ON_GOING", "STUDENT");
        SchemePolicyInput policy = new(false, "APPROVED", "QD-POL01", "POL_SINGLE_30_50_20", VersionedResultCalculator.PolV4Version,
            "AWAY_FROM_ZERO", 2, 4.00m, [new(1, 30m), new(2, 35m), new(3, 35m)]);
        var scheme = new EvaluationSchemeDto(10, 10, 1, 20, 30, "POL-01 Single", "PUBLISHED", 5m, "scheme-token", 40,
            [coldComp, defComp, indComp], [new(101, 201, 301)], VersionedResultCalculator.PolV4Version, policy, Now);

        EvaluationScoreRecord[] coldScores = [new(11, "Cold C1", null, 100, 10, 0, true, 8m, null)];
        EvaluationScoreRecord[] defScores = [new(11, "Def C1", null, 100, 10, 0, true, 8m, null)];
        var coldTotal = VersionedResultCalculator.EvaluationTotal(coldScores, VersionedResultCalculator.PolV4Version);
        var defTotal = VersionedResultCalculator.EvaluationTotal(defScores, VersionedResultCalculator.PolV4Version);

        // BindEvidenceAsync explicitly sets DeliverableVersionIds = [] for COLD stage.
        var coldEval = new EvaluationDraftRecord(50, 60, 20, 70, 5, "Rubric", 5, 1, "COMMITTEE", "FINALIZED", "Solid cold",
            EvaluationScoring.FinalTotal(coldScores), "cold-token", Now, Now, coldScores,
            new(70, Now, new(80, 81, Now, 1, 1, [], "COLD", 900, [901], "sha256-cold")));
        var defEval = new EvaluationDraftRecord(51, 61, 20, 70, 5, "Rubric", 5, 1, "COMMITTEE", "FINALIZED", "Solid def",
            EvaluationScoring.FinalTotal(defScores), "def-token", Now, Now, defScores,
            new(70, Now, new(80, 81, Now, 1, 1, [90], "DEFENSE")));

        var contributions = new[]
        {
            new ResultContributionDto(60, 50, 70, 5, 30m, coldTotal, coldEval.ConcurrencyToken),
            new ResultContributionDto(61, 51, 70, 5, 50m, defTotal, defEval.ConcurrencyToken)
        };
        var breakdownComponents = new ResultComponentScoreDto[]
        {
            new(1, "Cold", "COMMON", null, 30m, coldTotal, 1, 1, "COLD", "PROJECT"),
            new(2, "Defense", "COMMON", null, 50m, defTotal, 1, 1, "DEFENSE", "PROJECT"),
            new(3, "GVHD 20%", "INDIVIDUAL", 201, 20m, 8.50m * 30m / 100m + 8.50m * 35m / 100m + 8.50m * 35m / 100m, 0, 0, "ON_GOING", "STUDENT")
        };
        var raw = VersionedResultCalculator.Total(breakdownComponents, policy, true)!;
        var commonAvg = (coldTotal * 30m + defTotal * 50m) / 80m;
        var breakdown = new ResultBreakdownDto(scheme.Name, 1, VersionedResultCalculator.PolV4Version, false, false, "APPROVED", "READY",
            raw, commonAvg, null, [], breakdownComponents, OutcomeDetail: "PASSED");

        var frozen = JsonSerializer.Serialize(new
        {
            SnapshotVersion = 2,
            Breakdown = breakdown,
            Scheme = scheme,
            Target = scheme.Students[0],
            Policy = new { Id = 40, Status = "LOCKED" },
            PackageId = 80,
            Contributions = contributions,
            Evaluations = new object[]
            {
                new
                {
                    Id = 60L,
                    ScopeSnapshotJson = JsonSerializer.Serialize(new
                    {
                        Scheme = scheme,
                        component = coldComp,
                        StudentId = (long?)null,
                        FinalSubmissionId = 80L,
                        Cold = new { PackageId = 900L, Hash = "sha256-cold", SubmissionIds = new long[] { 901L } }
                    }),
                    evaluation = coldEval
                },
                new
                {
                    Id = 61L,
                    ScopeSnapshotJson = JsonSerializer.Serialize(new
                    {
                        Scheme = scheme,
                        component = defComp,
                        StudentId = (long?)null,
                        FinalSubmissionId = 80L
                    }),
                    evaluation = defEval
                },
                new
                {
                    Type = "GATE_INDIVIDUAL_PROCESS",
                    ComponentId = 3L,
                    StudentId = 101L,
                    Gates = new[]
                    {
                        new { Id = 701L, GateNumber = 1, Score = 8.50m, GateWeight = 30m, SupervisorAssignmentId = (long?)1L, FinalizedAt = (DateTime?)Now },
                        new { Id = 702L, GateNumber = 2, Score = 8.50m, GateWeight = 35m, SupervisorAssignmentId = (long?)1L, FinalizedAt = (DateTime?)Now },
                        new { Id = 703L, GateNumber = 3, Score = 8.50m, GateWeight = 35m, SupervisorAssignmentId = (long?)1L, FinalizedAt = (DateTime?)Now }
                    }
                }
            }
        });

        var baseline = ResultRevisionCalculator.Calculate(frozen, []);
        Assert.Equal(8.10m, baseline.TotalScore);

        var amended = ResultRevisionCalculator.Calculate(frozen, [new(50, 11, 9m, "Re-evaluated cold report")]);
        Assert.Equal(8.40m, amended.TotalScore);
    }
}
