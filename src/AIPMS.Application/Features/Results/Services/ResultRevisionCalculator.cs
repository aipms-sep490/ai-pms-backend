using System.Text.Json;
using System.Text.Json.Nodes;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Services;

// Recalculate from publication inputs, never today's rubric, evaluator or student profile.
public static class ResultRevisionCalculator
{
    private sealed record FrozenEvaluation(long Id, string ScopeSnapshotJson, EvaluationDraftRecord Evaluation);
    private sealed record FrozenGateScore(long Id, int GateNumber, decimal Score, decimal GateWeight, long? SupervisorAssignmentId, DateTime? FinalizedAt);
    private sealed record FrozenGateProcessEntry(long? ComponentId, long StudentId, FrozenGateScore[] Gates);
    private sealed record FrozenDefenseAbsenceEntry(long? ComponentId, long Id, long StudentId, string Reason, string? MinutesReference, DateTime RecordedAt, bool Revoked = false);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static ConflictException Unknown() => new("Published result lacks exact scoring provenance.", "RESULT_PROVENANCE_UNKNOWN");

    public static RevisionCalculation Calculate(string frozenInputs, IReadOnlyList<ScoreAmendmentInput> amendments) =>
        Calculate(frozenInputs, amendments, null, null);

    public static RevisionCalculation Calculate(
        string frozenInputs,
        IReadOnlyList<ScoreAmendmentInput> amendments,
        IReadOnlyList<GateProcessAmendmentInput>? gateAmendments,
        DefenseAbsenceAmendmentInput? absenceOverride = null)
    {
        try
        {
            var baseline = CalculateCore(frozenInputs, [], null, null);
            var original = JsonNode.Parse(frozenInputs)!["Breakdown"]!.Deserialize<ResultBreakdownDto>(Options)!;
            if (JsonSerializer.Serialize(baseline.Breakdown) != JsonSerializer.Serialize(original)) throw Unknown();
            var hasChanges = amendments.Count > 0 || (gateAmendments?.Count ?? 0) > 0 || absenceOverride is { RevokeAbsence: true };
            return !hasChanges ? baseline : CalculateCore(frozenInputs, amendments, gateAmendments, absenceOverride);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException
            or FormatException or ArgumentException or ArithmeticException or AIPMS.Domain.Exceptions.DomainException)
        { throw Unknown(); }
    }

    private static RevisionCalculation CalculateCore(
        string json,
        IReadOnlyList<ScoreAmendmentInput> amendments,
        IReadOnlyList<GateProcessAmendmentInput>? gateAmendments,
        DefenseAbsenceAmendmentInput? absenceOverride)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw Unknown();
        if (root["SnapshotVersion"]?.GetValue<int>() != 2) throw Unknown();
        var scheme = root["Scheme"]!.Deserialize<EvaluationSchemeDto>(Options) ?? throw Unknown();
        var policy = scheme.Policy ?? throw Unknown();
        if (scheme.Status != "PUBLISHED" || scheme.LockedAt is null || policy.IsDemo
            || policy.ApprovalStatus != "APPROVED" || string.IsNullOrWhiteSpace(policy.ApprovalReference)) throw Unknown();
        try
        {
            var componentInputs = scheme.Components.Select(c => new SchemeComponentInput(c.Name, c.Scope,
                c.MajorId, c.RubricId, c.ProjectWeightPercent, c.StudentWeightPercent, c.RequiredEvaluators, c.EvaluatorWeights, c.Stage!, c.TargetScope, c.IsIntegration)).ToArray();
            var majorIds = scheme.Students.Select(s => s.MajorId).Distinct().ToArray();
            EvaluationSchemeRules.Validate(componentInputs, majorIds, scheme.PassThreshold);
            VersionedResultCalculator.Validate(policy, componentInputs, majorIds);
        }
        catch (ValidationException) { throw Unknown(); }
        if (scheme.CalculationRule != VersionedResultCalculator.CalculationRuleFor(policy)
            || scheme.Components.Select(c => c.Id).Distinct().Count() != scheme.Components.Count
            || scheme.Components.Any(c => c.RequiredEvaluators <= 0 || c.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")
                || (c.Scope == "COMMON") != (c.MajorId is null) || c.Scope == "INDIVIDUAL" && c.ProjectWeightPercent != 0
                || c.ProjectWeightPercent < 0 || c.StudentWeightPercent < 0 || c.Stage is not ("COLD" or "DEFENSE" or "ON_GOING"))) throw Unknown();
        var target = root["Target"]?.Deserialize<SchemeStudent>(Options);
        if (target is not null && !scheme.Students.Contains(target)) throw Unknown();
        var package = root["PackageId"]!.GetValue<long>();
        if (package <= 0 || root["Policy"]!["Id"]!.GetValue<long>() != scheme.PolicyVersionId
            || root["Policy"]!["Status"]!.GetValue<string>() != "LOCKED") throw Unknown();
        var oldBreakdown = root["Breakdown"]!.Deserialize<ResultBreakdownDto>(Options) ?? throw Unknown();
        if (oldBreakdown.IsLegacy || oldBreakdown.Readiness != "READY" || oldBreakdown.CalculationVersion != policy.CalculationVersion) throw Unknown();
        var contributors = root["Contributions"]!.Deserialize<ResultContributionDto[]>(Options) ?? throw Unknown();
        var evalNodes = root["Evaluations"]?.AsArray() ?? throw Unknown();

        // TK-W4-01: Partition frozen Evaluations array into standard Rubric evaluations, POL-03 GATE_INDIVIDUAL_PROCESS, and DEFENSE_ABSENT records.
        var frozen = new List<FrozenEvaluation>();
        var frozenGates = new List<(JsonObject Node, FrozenGateProcessEntry Entry)>();
        var frozenAbsences = new List<(JsonObject Node, FrozenDefenseAbsenceEntry Entry)>();
        foreach (var item in evalNodes)
        {
            if (item is not JsonObject obj) throw Unknown();
            var entryType = obj["Type"]?.GetValue<string>();
            if (string.Equals(entryType, "GATE_INDIVIDUAL_PROCESS", StringComparison.Ordinal))
            {
                var entry = obj.Deserialize<FrozenGateProcessEntry>(Options) ?? throw Unknown();
                frozenGates.Add((obj, entry));
            }
            else if (string.Equals(entryType, "DEFENSE_ABSENT", StringComparison.Ordinal))
            {
                var entry = obj.Deserialize<FrozenDefenseAbsenceEntry>(Options) ?? throw Unknown();
                frozenAbsences.Add((obj, entry));
            }
            else if (string.IsNullOrEmpty(entryType))
            {
                var entry = obj.Deserialize<FrozenEvaluation>(Options) ?? throw Unknown();
                if (entry.Evaluation is null || string.IsNullOrWhiteSpace(entry.ScopeSnapshotJson)) throw Unknown();
                frozen.Add(entry);
            }
            else
            {
                throw Unknown();
            }
        }

        if (contributors.Select(c => c.EvaluationId).Distinct().Count() != contributors.Length) throw Unknown();
        if (amendments.Any(a => a is null || a.EvaluationId <= 0 || a.CriterionId <= 0 || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 2000)
            || amendments.Select(a => (a.EvaluationId, a.CriterionId)).Distinct().Count() != amendments.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["amendments"] = ["Unique evaluation/criterion and reason <=2000 required."] });

        var effectiveGateAmendments = gateAmendments ?? [];
        if (effectiveGateAmendments.Any(a => a is null || a.GateScoreId <= 0 || a.GateNumber is < 1 or > 3 || a.Score is < 0 or > 10
                || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 2000)
            || effectiveGateAmendments.Select(a => a.GateScoreId).Distinct().Count() != effectiveGateAmendments.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["gateAmendments"] = ["Unique gateScoreId, gate 1..3, score 0..10 and reason <=2000 required."] });

        var contributions = new List<ResultContributionDto>();
        var byComponent = new Dictionary<long, List<(decimal Score, decimal Weight)>>();
        var syntheticComponentScores = new Dictionary<long, decimal>();
        var evaluatorSlots = new HashSet<(long Component, long Evaluator)>();
        var changed = new Dictionary<long, EvaluationDraftRecord>();
        foreach (var original in contributors)
        {
            var records = frozen.Where(f => f.Evaluation.Id == original.EvaluationId && f.Id == original.AssignmentId).ToArray();
            if (records.Length == 0) throw Unknown();
            var row = records[0];
            if (records.Any(r => JsonSerializer.Serialize(r) != JsonSerializer.Serialize(row))) throw Unknown();
            var evaluation = row.Evaluation;
            using var assignment = JsonDocument.Parse(row.ScopeSnapshotJson);
            var componentId = assignment.RootElement.GetProperty("component").GetProperty("Id").GetInt64();
            var component = scheme.Components.Single(c => c.Id == componentId);
            var pinnedScheme = assignment.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>(Options)!;
            var pinnedComponent = assignment.RootElement.GetProperty("component").Deserialize<SchemeComponentDto>(Options)!;
            // ORM component snapshots contain persisted weight JSON, so compare stable authority fields explicitly.
            if (JsonSerializer.Serialize(pinnedScheme) != JsonSerializer.Serialize(scheme)
                || pinnedComponent.Scope != component.Scope || pinnedComponent.MajorId != component.MajorId
                || pinnedComponent.RubricId != component.RubricId || pinnedComponent.Stage != component.Stage
                || pinnedComponent.RequiredEvaluators != component.RequiredEvaluators
                || pinnedComponent.ProjectWeightPercent != component.ProjectWeightPercent
                || pinnedComponent.StudentWeightPercent != component.StudentWeightPercent
                || !evaluatorSlots.Add((componentId, original.EvaluatorId))) throw Unknown();
            var assignedStudent = assignment.RootElement.GetProperty("StudentId");
            if (evaluation.Status != "FINALIZED" || evaluation.Finalization?.Evidence.FinalSubmissionId != package
                || evaluation.AssignmentId != row.Id || evaluation.EvaluatorId != original.EvaluatorId
                || evaluation.RubricId != component.RubricId || original.RubricId != component.RubricId
                || evaluation.ProjectId != scheme.ProjectId || evaluation.Finalization.Evidence.Stage != component.Stage
                || component.Scope == "INDIVIDUAL" && (target is null || assignedStudent.GetInt64() != target.StudentId)
                || component.Scope != "COMMON" && target is not null && component.MajorId != target.MajorId
                || component.Scope != "INDIVIDUAL" && assignedStudent.ValueKind != JsonValueKind.Null
                || assignment.RootElement.GetProperty("FinalSubmissionId").GetInt64() != package
                || evaluation.Finalization.FinalizedBy != evaluation.EvaluatorId
                || evaluation.Finalization.Evidence.ProjectPeriodId <= 0
                || evaluation.Finalization.Evidence.FileCount <= 0 || evaluation.Finalization.Evidence.ArtifactCount <= 0
                || evaluation.Finalization.Evidence.DeliverableVersionIds.Count == 0
                || original.EvaluationConcurrencyToken != evaluation.ConcurrencyToken
                || EvaluationScoring.FinalTotal(evaluation.Scores) != evaluation.TotalScore) throw Unknown();
            if (component.Stage == "COLD")
            {
                var cold = assignment.RootElement.GetProperty("Cold");
                var evidence = evaluation.Finalization.Evidence;
                if (cold.GetProperty("PackageId").GetInt64() != evidence.ColdPackageId
                    || string.IsNullOrWhiteSpace(evidence.ColdEvidenceHash) || cold.GetProperty("Hash").GetString() != evidence.ColdEvidenceHash
                    || evidence.ColdSubmissionIds is null || evidence.ColdSubmissionIds.Count == 0
                    || !cold.GetProperty("SubmissionIds").Deserialize<long[]>()!.SequenceEqual(evidence.ColdSubmissionIds)) throw Unknown();
            }
            if (VersionedResultCalculator.EvaluationTotal(evaluation.Scores, policy.CalculationVersion) != original.Score) throw Unknown();
            var edits = amendments.Where(a => a.EvaluationId == evaluation.Id).ToDictionary(a => a.CriterionId);
            if (edits.Keys.Any(id => evaluation.Scores.All(c => c.RubricCriterionId != id))) throw Unknown();
            var scores = evaluation.Scores.Select(c => edits.TryGetValue(c.RubricCriterionId, out var edit) ? c with { Score = edit.Score } : c).ToArray();
            var total = VersionedResultCalculator.EvaluationTotal(scores, policy.CalculationVersion);
            changed[evaluation.Id] = evaluation with { Scores = scores, TotalScore = EvaluationScoring.FinalTotal(scores) };
            var weight = component.EvaluatorWeights?.Single(w => w.EvaluatorId == evaluation.EvaluatorId).WeightPercent
                ?? 100m / component.RequiredEvaluators;
            if (original.WeightPercent != (target is null ? component.ProjectWeightPercent : component.StudentWeightPercent) * weight / 100m) throw Unknown();
            if (!byComponent.TryGetValue(componentId, out var values)) byComponent[componentId] = values = [];
            values.Add((total, weight));
            contributions.Add(original with { Score = total });
        }
        if (amendments.Any(a => contributors.All(c => c.EvaluationId != a.EvaluationId)))
            throw new ConflictException("An amendment targets another result.", "AMENDMENT_TARGET_MISMATCH");

        var expectedComponents = scheme.Components.Where(c => target is null ? c.ProjectWeightPercent > 0
            : c.StudentWeightPercent > 0 && (c.Scope == "COMMON" || c.MajorId == target.MajorId)).OrderBy(c => c.Id).ToArray();

        // TK-W4-01: Resolve POL-03 GATE_INDIVIDUAL_PROCESS and DEFENSE_ABSENT components from frozen snapshot entries.
        var matchedGateAmendmentIds = new HashSet<long>();
        var isDefenseAbsent = false;
        var recoveredLookup = (absenceOverride?.RecoveredScores ?? [])
            .GroupBy(r => r.ComponentId)
            .ToDictionary(g => g.Key, g => g.Last().Score);

        if (target is not null)
        {
            foreach (var c in expectedComponents)
            {
                if (byComponent.ContainsKey(c.Id)) continue;

                // POL-03 20% Individual Process G1-G3
                if (c.Scope == "INDIVIDUAL" && c.Stage == "ON_GOING"
                    && policy.CalculationVersion == VersionedResultCalculator.PolV4Version)
                {
                    var gateMatch = frozenGates.FirstOrDefault(g => g.Entry.StudentId == target.StudentId
                        && (g.Entry.ComponentId is null || g.Entry.ComponentId == c.Id));
                    if (gateMatch.Node is null || gateMatch.Entry.Gates is null) throw Unknown();
                    var configuredGates = VersionedResultCalculator.EffectiveGateWeights(policy);
                    if (gateMatch.Entry.Gates.Length != configuredGates.Count) throw Unknown();

                    var updatedGates = new List<FrozenGateScore>(gateMatch.Entry.Gates.Length);
                    foreach (var gw in configuredGates)
                    {
                        var gateRow = gateMatch.Entry.Gates.SingleOrDefault(g => g.GateNumber == gw.GateNumber);
                        if (gateRow is null || gateRow.GateWeight != gw.WeightPercent || gateRow.Score is < 0 or > 10)
                            throw Unknown();
                        var edit = effectiveGateAmendments.SingleOrDefault(a => a.GateScoreId == gateRow.Id);
                        if (edit is not null)
                        {
                            if (edit.GateNumber != gateRow.GateNumber)
                                throw new ConflictException("Gate amendment does not match frozen gate number.", "AMENDMENT_TARGET_MISMATCH");
                            matchedGateAmendmentIds.Add(edit.GateScoreId);
                            gateRow = gateRow with { Score = edit.Score };
                        }
                        updatedGates.Add(gateRow);
                    }

                    var cumulative = updatedGates.Sum(g => g.Score * g.GateWeight / 100m);
                    syntheticComponentScores[c.Id] = cumulative;
                    if (matchedGateAmendmentIds.Count > 0)
                    {
                        gateMatch.Node["Gates"] = JsonSerializer.SerializeToNode(updatedGates);
                    }
                    continue;
                }

                // DEFENSE_ABSENT (explicit 0.00 or revoked via QD-04 appeal)
                if (c.Stage == "DEFENSE")
                {
                    var absMatch = frozenAbsences.FirstOrDefault(a => a.Entry.StudentId == target.StudentId
                        && !a.Entry.Revoked
                        && (a.Entry.ComponentId is null || a.Entry.ComponentId == c.Id));
                    if (absMatch.Node is not null)
                    {
                        if (absenceOverride is { RevokeAbsence: true }
                            && (absenceOverride.AbsenceId <= 0 || absenceOverride.AbsenceId == absMatch.Entry.Id))
                        {
                            if (!recoveredLookup.TryGetValue(c.Id, out var recoveredScore) || recoveredScore is < 0 or > 10)
                                throw new ConflictException("Revoking DEFENSE_ABSENT requires recovered defense score(s) between 0 and 10.", "DEFENSE_SCORE_REQUIRED");
                            syntheticComponentScores[c.Id] = recoveredScore;
                            absMatch.Node["Revoked"] = true;
                            absMatch.Node["RevokedReason"] = absenceOverride.Reason;
                            absMatch.Node["RecoveredScore"] = recoveredScore;
                        }
                        else
                        {
                            isDefenseAbsent = true;
                            syntheticComponentScores[c.Id] = 0.00m;
                        }
                    }
                    else
                    {
                        // Check previously revoked absence node that already recorded a recovered score in a prior revision
                        var priorRevoked = frozenAbsences.FirstOrDefault(a => a.Entry.StudentId == target.StudentId
                            && a.Entry.Revoked
                            && (a.Entry.ComponentId is null || a.Entry.ComponentId == c.Id));
                        if (priorRevoked.Node is not null && priorRevoked.Node["RecoveredScore"] is JsonNode recNode)
                        {
                            syntheticComponentScores[c.Id] = recNode.GetValue<decimal>();
                        }
                    }
                }
            }
        }

        if (effectiveGateAmendments.Any(a => !matchedGateAmendmentIds.Contains(a.GateScoreId)))
            throw new ConflictException("A gate amendment targets another result or missing gate score.", "AMENDMENT_TARGET_MISMATCH");

        var resolvedComponentIds = byComponent.Keys.Concat(syntheticComponentScores.Keys).Distinct().Order().ToArray();
        if (!expectedComponents.Select(c => c.Id).Order().SequenceEqual(resolvedComponentIds)) throw Unknown();

        var components = expectedComponents.Select(c =>
        {
            if (syntheticComponentScores.TryGetValue(c.Id, out var syntheticScore))
            {
                return new ResultComponentScoreDto(c.Id, c.Name, c.Scope, c.MajorId,
                    target is null ? c.ProjectWeightPercent : c.StudentWeightPercent,
                    syntheticScore,
                    c.RequiredEvaluators, c.RequiredEvaluators, c.Stage, c.TargetScope, c.IsIntegration);
            }
            var values = byComponent[c.Id];
            if (values.Count != c.RequiredEvaluators) throw Unknown();
            return new ResultComponentScoreDto(c.Id, c.Name, c.Scope, c.MajorId,
                target is null ? c.ProjectWeightPercent : c.StudentWeightPercent,
                c.EvaluatorWeights is null ? values.Average(v => v.Score) : values.Sum(v => v.Score * v.Weight / 100m),
                c.RequiredEvaluators, values.Count, c.Stage, c.TargetScope, c.IsIntegration);
        }).ToArray();

        var raw = VersionedResultCalculator.Total(components, policy, target is not null) ?? throw Unknown();
        var final = VersionedResultCalculator.Round(raw, policy);
        var common = components.Where(c => c.Scope == "COMMON").ToArray();
        var major = components.Where(c => c.Scope == "MAJOR_SPECIFIC").ToArray();
        var majorNames = oldBreakdown.MajorBreakdown.ToDictionary(m => m.MajorId, m => m.MajorName);
        var majors = major.GroupBy(c => c.MajorId!.Value).OrderBy(g => g.Key)
            .Select(g => new ResultMajorScoreDto(g.Key, g.Sum(c => c.Score!.Value * c.WeightPercent) / g.Sum(c => c.WeightPercent),
                majorNames.GetValueOrDefault(g.Key))).ToArray();
        var effectiveAbsent = absenceOverride is { RevokeAbsence: true } ? false : (isDefenseAbsent || oldBreakdown.OutcomeDetail == "DEFENSE_ABSENT");
        var (_, outcomeDetail) = VersionedResultCalculator.EvaluateOutcome(final, scheme.PassThreshold, components, policy, effectiveAbsent);
        var breakdown = oldBreakdown with
        {
            UnroundedTotal = raw,
            Components = components,
            MajorBreakdown = majors,
            CommonScore = common.Length == 0 ? null : common.Sum(c => c.Score!.Value * c.WeightPercent) / common.Sum(c => c.WeightPercent),
            MajorAggregate = major.Length == 0 ? null : major.Sum(c => c.Score!.Value * c.WeightPercent) / major.Sum(c => c.WeightPercent),
            OutcomeDetail = oldBreakdown.OutcomeDetail is not null || policy.CalculationVersion == VersionedResultCalculator.PolV4Version ? outcomeDetail : oldBreakdown.OutcomeDetail
        };
        root["Breakdown"] = JsonSerializer.SerializeToNode(breakdown);
        root["Contributions"] = JsonSerializer.SerializeToNode(contributions);
        foreach (var entry in evalNodes)
        {
            if (entry is JsonObject node && node["evaluation"] is JsonObject evalObj && evalObj["Id"] is JsonNode idNode)
            {
                var id = idNode.GetValue<long>();
                if (changed.TryGetValue(id, out var value)) node["evaluation"] = JsonSerializer.SerializeToNode(value);
            }
        }
        return new(root.ToJsonString(), final, breakdown, contributions);
    }
}
