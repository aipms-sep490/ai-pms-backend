using System.Text.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.UnitTests.Application;

public sealed class VersionedResultCalculatorTests
{
    private static ResultComponentScoreDto Component(long id, string scope, long? major, decimal weight, decimal? score) =>
        new(id, scope, scope, major, weight, score, 1, score.HasValue ? 1 : 0);

    [Theory]
    [InlineData(9, 7, 8.10)]
    [InlineData(7, 9, 7.90)]
    [InlineData(6, 8, 7.40)]
    public void Golden_student_demo_is_50_common_30_own_major_20_individual(decimal major, decimal individual, decimal expected)
    {
        var policy = new SchemePolicyInput(Formula: "DEMO_50_30_20");
        var raw = VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 8),
            Component(2, "MAJOR_SPECIFIC", 1, 30, major), Component(3, "INDIVIDUAL", 1, 20, individual)], policy, true);
        Assert.Equal(expected, VersionedResultCalculator.Round(raw!.Value, policy));
    }

    [Fact]
    public void Golden_project_demo_uses_unrounded_major_mean_without_individual_or_industry()
    {
        var policy = new SchemePolicyInput(Formula: "DEMO_50_30_20");
        var raw = VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 8),
            Component(2, "MAJOR_SPECIFIC", 1, 16.6666m, 9), Component(3, "MAJOR_SPECIFIC", 2, 16.6667m, 7),
            Component(4, "MAJOR_SPECIFIC", 3, 16.6667m, 6)], policy, false);
        Assert.Equal(4m + (22m / 3m) * .5m, raw);
        Assert.Equal(7.67m, VersionedResultCalculator.Round(raw!.Value, policy));
    }

    [Fact]
    public void Missing_score_is_pending_and_zero_remains_zero_without_reweighting()
    {
        var policy = new SchemePolicyInput();
        Assert.Null(VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 10),
            Component(2, "MAJOR_SPECIFIC", 1, 50, null)], policy, false));
        Assert.Equal(5m, VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 10),
            Component(2, "MAJOR_SPECIFIC", 1, 50, 0)], policy, false));
        Assert.Null(VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 10)], policy, false));
    }

    [Fact]
    public void V2_keeps_raw_criterion_total_while_explicit_v1_preserves_historical_rounding()
    {
        EvaluationScoreRecord[] scores = [new(1, "A", null, 100, 3, 1, true, 1, null)];
        Assert.Equal(3.33m, VersionedResultCalculator.EvaluationTotal(scores, "LEGACY_V1"));
        var raw = VersionedResultCalculator.EvaluationTotal(scores, "DECIMAL_V2");
        Assert.True(raw > 3.33m);
        Assert.Equal(1.675m, VersionedResultCalculator.Total([Component(1, "COMMON", null, 50, 3.33m),
            Component(2, "COMMON", null, 50, .02m)], new(CalculationVersion: "LEGACY_V1"), false));
        Assert.Equal(1.68m, VersionedResultCalculator.Round(1.675m, new()));
        Assert.Equal(1.68m, VersionedResultCalculator.Round(1.685m, new(RoundingMode: "TO_EVEN")));
        Assert.Equal(1.69m, VersionedResultCalculator.Round(1.685m, new()));
    }

    [Fact]
    public void Approval_and_evaluator_weight_configuration_fail_closed()
    {
        SchemeComponentInput c = new("Common", "COMMON", null, 1, 100, 100, 2,
            [new(10, 75), new(20, 25)]);
        VersionedResultCalculator.Validate(new(), [c], [1]);
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(new(false, "APPROVED"), [c], [1]));
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(new(),
            [c with { EvaluatorWeights = [new(10, 75), new(10, 25)] }], [1]));
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(new(),
            [c with { EvaluatorWeights = [new(10, 75)] }], [1]));
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(new(),
            [c with { EvaluatorWeights = [new(10, 50), new(20, 20)] }], [1]));
        Assert.Throws<ValidationException>(() => VersionedResultCalculator.Validate(new(CalculationVersion: "LEGACY_V1"), [c], [1]));
    }

    [Fact]
    public void Historical_projection_is_typed_target_checked_and_never_exposes_raw_comments_or_roster()
    {
        var breakdown = new ResultBreakdownDto("Frozen scheme", 3, "DECIMAL_V2", false, false, "APPROVED", "READY", 8.1m,
            8m, 9m, [new(1, 9), new(2, 7)], [Component(1, "COMMON", null, 50, 8),
            Component(2, "MAJOR_SPECIFIC", 1, 30, 9), Component(3, "INDIVIDUAL", 1, 20, 7),
            Component(4, "MAJOR_SPECIFIC", 2, 30, 7)]);
        var json = JsonSerializer.Serialize(new { SnapshotVersion = 2, Breakdown = breakdown,
            Target = new SchemeStudent(123, 1, 99), SecretOtherStudentComment = "private", EvaluatorId = 999 });
        var result = ResultSnapshotReader.Read(json, "DECIMAL_V2", 123, 1);
        Assert.Equal("Frozen scheme", result.SchemeName);
        Assert.Equal(3, result.Components.Count);
        Assert.Single(result.MajorBreakdown);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
        Assert.True(ResultSnapshotReader.Read(json, "DECIMAL_V2", 456, 1).IsLegacy);
        Assert.Equal(99, ResultSnapshotReader.StudentDepartment(json, 123));
        Assert.Null(ResultSnapshotReader.StudentDepartment(json, 456));
        Assert.DoesNotContain(ResultSnapshotReader.Read(json, "DECIMAL_V2").Components, c => c.Scope == "INDIVIDUAL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("invalid json")]
    [InlineData("{\"SnapshotVersion\":2,\"Breakdown\":null}")]
    public void Legacy_unknown_is_not_reconstructed_from_current_configuration(string? json)
    {
        var result = ResultSnapshotReader.Read(json, "old");
        Assert.True(result.IsLegacy);
        Assert.Equal("UNKNOWN", result.ApprovalStatus);
        Assert.Null(result.UnroundedTotal);
        Assert.Empty(result.Components);
    }

    [Fact]
    public void Pol01_single_major_30_50_20_and_failing_floor_below_4_is_deterministic()
    {
        var policy = new SchemePolicyInput(false, "APPROVED", "QD-POL01-2026", "POL_SINGLE_30_50_20", VersionedResultCalculator.PolV4Version);
        SchemeComponentInput[] inputs =
        [
            new("Cold Report", "COMMON", null, 1, 37.5m, 30m, 2, Stage: "COLD", TargetScope: "PROJECT"),
            new("Defense", "COMMON", null, 2, 62.5m, 50m, 2, Stage: "DEFENSE", TargetScope: "PROJECT"),
            new("GVHD Process", "INDIVIDUAL", 1, 3, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
        ];
        VersionedResultCalculator.Validate(policy, inputs, [1]);

        ResultComponentScoreDto[] passing =
        [
            new(1, "Cold", "COMMON", null, 30m, 7.5m, 2, 2, "COLD", "PROJECT"),
            new(2, "Defense", "COMMON", null, 50m, 8.0m, 2, 2, "DEFENSE", "PROJECT"),
            new(3, "GVHD", "INDIVIDUAL", 1, 20m, 6.5m, 1, 1, "ON_GOING", "STUDENT")
        ];
        var rawPass = VersionedResultCalculator.Total(passing, policy, true)!.Value;
        var roundedPass = VersionedResultCalculator.Round(rawPass, policy);
        Assert.Equal(7.55m, roundedPass);
        Assert.Equal(("PASSED", "PASSED"), VersionedResultCalculator.EvaluateOutcome(roundedPass, 5.00m, passing, policy));

        // Total = 3.75*0.3 + 8.0*0.5 + 7.5*0.2 = 1.125 + 4.0 + 1.5 = 6.63 >= 5.00, but Cold < 4.00 => FAILED_COMPONENT_FLOOR
        ResultComponentScoreDto[] floorFailed =
        [
            new(1, "Cold", "COMMON", null, 30m, 3.75m, 2, 2, "COLD", "PROJECT"),
            new(2, "Defense", "COMMON", null, 50m, 8.0m, 2, 2, "DEFENSE", "PROJECT"),
            new(3, "GVHD", "INDIVIDUAL", 1, 20m, 7.5m, 1, 1, "ON_GOING", "STUDENT")
        ];
        var roundedFloor = VersionedResultCalculator.Round(VersionedResultCalculator.Total(floorFailed, policy, true)!.Value, policy);
        Assert.Equal(6.63m, roundedFloor);
        Assert.Equal(("FAILED", "FAILED_COMPONENT_FLOOR"), VersionedResultCalculator.EvaluateOutcome(roundedFloor, 5.00m, floorFailed, policy));

        // Explicit Defense absence => FAILED / DEFENSE_ABSENT
        Assert.Equal(("FAILED", "DEFENSE_ABSENT"), VersionedResultCalculator.EvaluateOutcome(roundedPass, 5.00m, passing, policy, defenseAbsent: true));
    }

    [Fact]
    public void Pol01_interdisciplinary_40_40_20_splits_common_30_cold_70_defense_and_enforces_floor()
    {
        var policy = new SchemePolicyInput(false, "APPROVED", "QD-POL01-INTER", "POL_INTER_40_40_20", VersionedResultCalculator.PolV4Version,
            IndividualGateWeights: [new(1, 20m), new(2, 35m), new(3, 45m)]);
        SchemeComponentInput[] inputs =
        [
            new("Common Cold", "COMMON", null, 1, 15m, 12m, 2, Stage: "COLD", TargetScope: "PROJECT", IsIntegration: true),
            new("Common Defense", "COMMON", null, 2, 35m, 28m, 2, Stage: "DEFENSE", TargetScope: "PROJECT", IsIntegration: true),
            new("Major IT", "MAJOR_SPECIFIC", 1, 3, 25m, 40m, 1, Stage: "DEFENSE", TargetScope: "MAJOR"),
            new("Major MKT", "MAJOR_SPECIFIC", 2, 4, 25m, 40m, 1, Stage: "DEFENSE", TargetScope: "MAJOR"),
            new("Indiv IT", "INDIVIDUAL", 1, 5, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT"),
            new("Indiv MKT", "INDIVIDUAL", 2, 6, 0m, 20m, 1, Stage: "ON_GOING", TargetScope: "STUDENT")
        ];
        VersionedResultCalculator.Validate(policy, inputs, [1, 2]);

        ResultComponentScoreDto[] itStudent =
        [
            new(1, "Common Cold", "COMMON", null, 12m, 7.0m, 2, 2, "COLD", "PROJECT", true),
            new(2, "Common Defense", "COMMON", null, 28m, 8.0m, 2, 2, "DEFENSE", "PROJECT", true),
            new(3, "Major IT", "MAJOR_SPECIFIC", 1, 40m, 9.0m, 1, 1, "DEFENSE", "MAJOR"),
            new(5, "Indiv IT", "INDIVIDUAL", 1, 20m, 8.5m, 1, 1, "ON_GOING", "STUDENT")
        ];
        // 12%*7 + 28%*8 + 40%*9 + 20%*8.5 = 0.84 + 2.24 + 3.60 + 1.70 = 8.38
        var itTotal = VersionedResultCalculator.Round(VersionedResultCalculator.Total(itStudent, policy, true)!.Value, policy);
        Assert.Equal(8.38m, itTotal);
        Assert.Equal(("PASSED", "PASSED"), VersionedResultCalculator.EvaluateOutcome(itTotal, 5.00m, itStudent, policy));

        // Major MKT student with Major MKT = 3.50 < 4.00 => total = 3.08 + 1.40 + 1.70 = 6.18 >= 5.00, fails component floor!
        ResultComponentScoreDto[] mktStudent =
        [
            new(1, "Common Cold", "COMMON", null, 12m, 7.0m, 2, 2, "COLD", "PROJECT", true),
            new(2, "Common Defense", "COMMON", null, 28m, 8.0m, 2, 2, "DEFENSE", "PROJECT", true),
            new(4, "Major MKT", "MAJOR_SPECIFIC", 2, 40m, 3.5m, 1, 1, "DEFENSE", "MAJOR"),
            new(6, "Indiv MKT", "INDIVIDUAL", 2, 20m, 8.5m, 1, 1, "ON_GOING", "STUDENT")
        ];
        var mktTotal = VersionedResultCalculator.Round(VersionedResultCalculator.Total(mktStudent, policy, true)!.Value, policy);
        Assert.Equal(6.18m, mktTotal);
        Assert.Equal(("FAILED", "FAILED_COMPONENT_FLOOR"), VersionedResultCalculator.EvaluateOutcome(mktTotal, 5.00m, mktStudent, policy));
    }
}
