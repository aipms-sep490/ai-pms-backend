using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

// Internal foundation: only the approved appeal/correction workflow may call mutation methods.
internal sealed class ResultRevisionStore(AipmsDbContext db, IAuditTrail audit, TimeProvider clock)
{
    internal sealed record Target(long Key, int Version, long? RevisionId, string Inputs, string ResultJson);
    internal sealed record Calculated(Target Current, RevisionCalculation Calculation);
    internal sealed record Preview(string ConfirmationToken, IReadOnlyList<Calculated> Targets);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private void Transaction()
    { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Revision mutation requires a locked project transaction."); }
    internal async Task<Target[]> Current(long projectId, CancellationToken ct)
    {
        var project = await PublishedResultResolver.Project(db, projectId, ct);
        var students = await db.Set<StudentResult>().AsNoTracking().Where(r => r.ProjectId == projectId).OrderBy(r => r.StudentId).ToArrayAsync(ct);
        var revisions = await db.Set<ResultRevision>().AsNoTracking().Where(r => r.ProjectId == projectId && r.StudentId != null
            && !db.Set<ResultRevision>().Any(n => n.ProjectId == projectId && n.TargetKey == r.TargetKey && n.Version > r.Version)).ToArrayAsync(ct);
        var targets = new List<Target>();
        if (project is not null) targets.Add(new(0, project.Version, project.RevisionId, project.FrozenInputs ?? "{}", JsonSerializer.Serialize(project)));
        foreach (var row in students)
        {
            var revision = revisions.SingleOrDefault(r => r.StudentId == row.StudentId);
            var student = revision is null ? PublishedResultResolver.Root(row) : PublishedResultResolver.Student(revision);
            targets.Add(new(student.StudentId, student.Version, student.RevisionId, student.SnapshotJson, JsonSerializer.Serialize(student)));
        }
        return targets.ToArray();
    }
    internal static string Stamp(IEnumerable<Target> targets) => Hash(JsonSerializer.Serialize(targets.OrderBy(t => t.Key)));
    internal async Task<ResultCorrectionBundle> Create(long projectId, long maker, string reason,
        IReadOnlyList<ScoreAmendmentInput> amendments, CancellationToken ct)
    {
        Transaction();
        await AcademicAssignmentGuard.LockProjectAsync(db, projectId, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || amendments is null || amendments.Count is < 1 or > 100
            || amendments.Any(a => a is null))
            throw new ValidationException(new Dictionary<string, string[]> { ["amendments"] = ["A reason and 1..100 criterion amendments are required."] });
        var targets = await Current(projectId, ct);
        Calculate(targets, amendments);
        var row = new ResultCorrectionBundle { ProjectId = projectId, CreatedBy = maker, CreatedAt = clock.GetUtcNow().UtcDateTime,
            Reason = reason.Trim(), BaseStamp = Stamp(targets), Amendments = amendments.Select(a => new ResultScoreAmendment {
                EvaluationId = a.EvaluationId, CriterionId = a.CriterionId, Score = a.Score, Reason = a.Reason.Trim() }).ToArray() };
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(maker, "RESULT_CORRECTION_DRAFTED", "RESULT_CORRECTION_BUNDLE", row.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["amendmentCount"] = amendments.Count }), ct);
        return row;
    }
    internal static Calculated[] Calculate(Target[] targets, IReadOnlyList<ScoreAmendmentInput> amendments)
    {
        try { return CalculateCore(targets, amendments); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or NullReferenceException
            or FormatException or ArgumentException or ArithmeticException)
        { throw new ConflictException("Published result lacks exact scoring provenance.", "RESULT_PROVENANCE_UNKNOWN"); }
    }
    private static Calculated[] CalculateCore(Target[] targets, IReadOnlyList<ScoreAmendmentInput> amendments)
    {
        if (targets.Length == 0) throw new ConflictException("A published result is required.");
        var edits = amendments.Select(a => a.EvaluationId).Distinct().ToArray();
        var affected = new Dictionary<long, HashSet<long>>();
        var provenance = new Dictionary<long, string>();
        foreach (var target in targets)
        {
            using var root = JsonDocument.Parse(target.Inputs);
            if (!root.RootElement.TryGetProperty("Contributions", out var contribution)) continue;
            var ids = contribution.Deserialize<ResultContributionDto[]>()!.Select(c => c.EvaluationId).Intersect(edits).ToArray();
            if (ids.Length == 0) continue;
            var scheme = root.RootElement.GetProperty("Scheme").Deserialize<EvaluationSchemeDto>()!;
            foreach (var id in ids)
            {
                var entry = root.RootElement.GetProperty("Evaluations").EnumerateArray()
                    .First(e => e.GetProperty("evaluation").GetProperty("Id").GetInt64() == id);
                using var assignment = JsonDocument.Parse(entry.GetProperty("ScopeSnapshotJson").GetString()!);
                var fingerprint = JsonSerializer.Serialize(new { Scheme = scheme, Evaluation = entry,
                    PackageId = root.RootElement.GetProperty("PackageId").GetInt64() });
                if (provenance.TryGetValue(id, out var previous) && previous != fingerprint)
                    throw new ConflictException("Affected results have inconsistent frozen inputs.", "RESULT_PROVENANCE_UNKNOWN");
                provenance[id] = fingerprint;
                var component = scheme.Components.Single(c => c.Id == assignment.RootElement.GetProperty("component").GetProperty("Id").GetInt64());
                var expected = new List<long>();
                if (component.ProjectWeightPercent > 0 && component.Scope != "INDIVIDUAL") expected.Add(0);
                if (component.StudentWeightPercent > 0)
                {
                    var studentId = component.Scope == "INDIVIDUAL" ? assignment.RootElement.GetProperty("StudentId").GetInt64() : (long?)null;
                    expected.AddRange(scheme.Students.Where(s => component.Scope == "COMMON" || s.MajorId == component.MajorId)
                        .Where(s => studentId is null || s.StudentId == studentId).Select(s => s.StudentId));
                }
                if (expected.Any(key => targets.All(t => t.Key != key)))
                    throw new ConflictException("Publish all affected original results before correction.", "AFFECTED_RESULTS_MISSING");
                if (!affected.TryGetValue(id, out var keys)) affected[id] = keys = [];
                keys.UnionWith(expected);
            }
        }
        if (edits.Any(id => !affected.ContainsKey(id))) throw new ConflictException("Amendment does not belong to a published result.", "AMENDMENT_TARGET_MISMATCH");
        return targets.Where(t => affected.Any(a => a.Value.Contains(t.Key))).Select(t => {
            var baseline = ResultRevisionCalculator.Calculate(t.Inputs, []);
            using var result = JsonDocument.Parse(t.ResultJson);
            if (baseline.TotalScore != result.RootElement.GetProperty("TotalScore").GetDecimal())
                throw new ConflictException("Published total does not match frozen inputs.", "RESULT_PROVENANCE_UNKNOWN");
            return new Calculated(t, ResultRevisionCalculator.Calculate(t.Inputs,
                amendments.Where(a => affected[a.EvaluationId].Contains(t.Key)).ToArray()));
        }).ToArray();
    }
    internal async Task<Preview> Check(ResultCorrectionBundle bundle, CancellationToken ct)
    {
        var targets = await Current(bundle.ProjectId, ct);
        if (bundle.Status != "DRAFT" || Stamp(targets) != bundle.BaseStamp)
            throw new ConflictException("Published result version changed; review again.", "RESULT_REVISION_STALE");
        // SQL decimal scale must not change a confirmation token after a new request loads the draft.
        var amendments = bundle.Amendments.Select(a => new ScoreAmendmentInput(a.EvaluationId, a.CriterionId,
            decimal.Parse(a.Score.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture), a.Reason))
            .OrderBy(a => a.EvaluationId).ThenBy(a => a.CriterionId).ToArray();
        var calculated = Calculate(targets, amendments);
        var token = Hash(JsonSerializer.Serialize(new { bundle.Id, bundle.ConcurrencyToken, bundle.BaseStamp,
            Amendments = amendments.OrderBy(a => a.EvaluationId).ThenBy(a => a.CriterionId), Targets = calculated }));
        return new(token, calculated);
    }
    internal async Task<IReadOnlyList<ResultRevision>> Publish(ResultCorrectionBundle bundle, long publisher, string confirmation, CancellationToken ct)
    {
        Transaction();
        await AcademicAssignmentGuard.LockProjectAsync(db, bundle.ProjectId, ct);
        await db.Entry(bundle).ReloadAsync(ct);
        await db.Entry(bundle).Collection(b => b.Amendments).LoadAsync(ct);
        if (bundle.CreatedBy == publisher || await db.Evaluations.AnyAsync(e => bundle.Amendments.Select(a => a.EvaluationId).Contains(e.Id) && e.EvaluatorId == publisher, ct))
            throw new ForbiddenException("Publisher must be independent of the maker and original scorer.");
        var preview = await Check(bundle, ct);
        if (preview.Targets.Any(t => t.Calculation.Contributions.Any(c => c.EvaluatorId == publisher
            && bundle.Amendments.Any(a => a.EvaluationId == c.EvaluationId))))
            throw new ForbiddenException("The original scorer cannot approve their own amendment.");
        if (!string.Equals(preview.ConfirmationToken, confirmation, StringComparison.Ordinal))
            throw new ConflictException("Correction preview changed.", "RESULT_REVISION_STALE");
        var now = clock.GetUtcNow().UtcDateTime;
        var revisions = preview.Targets.Select(t => {
            var version = t.Current.Version + 1;
            string snapshot;
            if (t.Current.Key == 0) {
                var old = JsonSerializer.Deserialize<ProjectResultDto>(t.Current.ResultJson)!;
                snapshot = JsonSerializer.Serialize(old with { TotalScore = t.Calculation.TotalScore,
                    Outcome = t.Calculation.TotalScore >= old.PassThreshold ? "PASSED" : "FAILED", PublishedBy = publisher, PublishedAt = now,
                    Version = version, SupersedesVersion = t.Current.Version, RevisionId = null,
                    FrozenInputs = t.Calculation.FrozenInputs, Breakdown = t.Calculation.Breakdown, Contributions = t.Calculation.Contributions });
            } else {
                var old = JsonSerializer.Deserialize<StudentResultDto>(t.Current.ResultJson)!;
                snapshot = JsonSerializer.Serialize(old with { TotalScore = t.Calculation.TotalScore,
                    Outcome = t.Calculation.TotalScore >= old.PassThreshold ? "PASSED" : "FAILED", PublishedBy = publisher, PublishedAt = now,
                    Version = version, SupersedesVersion = t.Current.Version, RevisionId = null,
                    SnapshotJson = t.Calculation.FrozenInputs, Breakdown = t.Calculation.Breakdown });
            }
            return new ResultRevision { BundleId = bundle.Id, ProjectId = bundle.ProjectId, TargetKey = t.Current.Key,
                StudentId = t.Current.Key == 0 ? null : t.Current.Key, Version = version, PreviousRevisionId = t.Current.RevisionId,
                SnapshotJson = snapshot, PublishedBy = publisher, PublishedAt = now };
        }).ToArray();
        bundle.Status = "PUBLISHED"; bundle.PublishedBy = publisher; bundle.PublishedAt = now; bundle.ConcurrencyToken = Guid.NewGuid();
        db.AddRange(revisions); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(publisher, "RESULT_CORRECTION_PUBLISHED", "RESULT_CORRECTION_BUNDLE", bundle.Id,
            new Dictionary<string, object?> { ["projectId"] = bundle.ProjectId, ["revisionIds"] = revisions.Select(r => r.Id).ToArray() }), ct);
        return revisions;
    }
}
