using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Cold;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ColdSubmissionService
{
    public Task<ColdProgressDto> Progress(long projectId, CancellationToken ct) =>
        evaluations.InTransactionAsync(async () =>
        {
            var visible = await ProgressCore(projectId, ct);
            var all = await ProgressCore(projectId, ct, system: true);
            return visible with { Ready = all.Ready,
                Blockers = all.Ready ? visible.Blockers : visible.Blockers.Concat(["COLD_PROJECT_NOT_READY"]).Distinct().ToArray() };
        }, ct);

    internal async Task<ColdProgressDto> ProgressCore(long projectId, CancellationToken ct, bool system = false)
    {
        var package = await db.Set<ColdPackage>().SingleOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (package is null)
        {
            await Scope(projectId, null, ct, system);
            return new ColdProgressDto(projectId, "MISSING", 0, 0, 0, false, ["COLD_PACKAGE_REQUIRED"], []);
        }
        var access = await Scope(projectId, package, ct, system);
        var checklist = await ChecklistDto(package, access, ct);
        var scheme = await db.Set<EvaluationScheme>().AsNoTracking().Include(s => s.Components)
            .SingleOrDefaultAsync(s => s.ProjectId == projectId && s.Status == "PUBLISHED", ct);
        var rows = new List<ColdGradingProgressDto>();
        var blockers = new List<string>();
        var isStudentViewer = !system && access.StudentMajor.HasValue && !access.Internal && !access.Lead
            && !access.Primary && access.MentorMajors.Count == 0 && !access.Actor.IsStaff;
        var studentResultPublished = !isStudentViewer || await db.Set<StudentResult>().AsNoTracking()
            .AnyAsync(r => r.ProjectId == projectId && r.StudentId == Actor, ct);
        if (package.Status != "LOCKED") blockers.Add("COLD_PACKAGE_REQUIRED");
        if (scheme is null) blockers.Add("COLD_SCHEME_REQUIRED");
        else
        {
            var components = scheme.Components.Where(c => c.Stage == "COLD" && (isStudentViewer
                ? (c.Scope == "COMMON" || (c.MajorId.HasValue && access.StudentMajor == c.MajorId))
                : (access.Internal || access.Lead || access.Primary
                    || c.Scope == "COMMON" && access.StudentMajor.HasValue
                    || c.MajorId.HasValue && (access.MentorMajors.Contains(c.MajorId.Value) || access.StudentMajor == c.MajorId
                        || access.Actor.IsStaff && access.Registration.MajorDepartmentIds!.GetValueOrDefault(c.MajorId.Value) == access.Actor.DepartmentId))))
                .OrderBy(c => c.Id).ToArray();
            var ids = components.Select(c => c.Id).ToArray();
            var assignments = await db.Set<EvaluationAssignment>().AsNoTracking().Where(a => a.ProjectId == projectId
                && a.Status == "ACTIVE" && a.ComponentId.HasValue && ids.Contains(a.ComponentId.Value)
                && (access.StudentMajor == null || a.StudentId == null || a.StudentId == Actor)
                && a.Stage == "COLD" && a.ColdPackageId == package.Id).ToArrayAsync(ct);
            var finals = await (from state in db.Set<EvaluationDraftState>().AsNoTracking()
                join final in db.Set<EvaluationFinalization>().AsNoTracking() on state.EvaluationId equals final.EvaluationId
                where assignments.Select(a => a.Id).Contains(state.AssignmentId)
                select new { state.AssignmentId, final.SnapshotJson }).ToDictionaryAsync(f => f.AssignmentId,
                    f => JsonSerializer.Deserialize<EvaluationDraftRecord>(f.SnapshotJson), ct);
            var policy = scheme.GovernanceJson is null ? null : JsonSerializer.Deserialize<SchemePolicyInput>(scheme.GovernanceJson);
            var evidence = package.Status == "LOCKED" ? await ColdEvaluationBinding.Load(db, projectId, ct) : null;
            foreach (var c in components)
            {
                var targets = c.Scope == "INDIVIDUAL" ? access.Registration.Members.Where(s => s.MajorId == c.MajorId
                    && (access.StudentMajor is null || s.UserId == Actor)).Select(s => (long?)s.UserId).ToArray() : [null];
                foreach (var student in targets)
                {
                    var assigned = assignments.Where(a => a.ComponentId == c.Id && a.StudentId == student).ToArray();
                    var complete = new List<EvaluationDraftRecord>();
                    foreach (var assignment in assigned)
                    {
                        var final = finals.GetValueOrDefault(assignment.Id);
                        if (evidence is null || final?.Status != "FINALIZED" || final.Finalization is null) continue;
                        try
                        {
                            await DefenseCommitteeAuthority.RequirePinnedAsync(db, assignment, ct);
                            var binding = ColdEvaluationBinding.Validate(evidence, assignment);
                            if (assignment.Scope == c.Scope && assignment.MajorId == c.MajorId && assignment.RubricId == c.RubricId
                                && assignment.PolicyVersionId == scheme.PolicyVersionId && assignment.ProjectPeriodId == scheme.ProjectPeriodId
                                && final.AssignmentId == assignment.Id && final.ProjectId == projectId && final.EvaluatorId == assignment.EvaluatorId
                                && final.RubricId == c.RubricId && final.Finalization.Evidence.Stage == "COLD"
                                && final.Finalization.Evidence.ColdPackageId == binding.PackageId && final.Finalization.Evidence.ColdEvidenceHash == binding.Hash
                                && (final.Finalization.Evidence.ColdSubmissionIds ?? []).SequenceEqual(binding.SubmissionIds)) complete.Add(final);
                        }
                        catch (ConflictException ex) when (ex.Code is "DEFENSE_COMMITTEE_REQUIRED" or "DEFENSE_COMMITTEE_BINDING_REQUIRED" or "DEFENSE_COMMITTEE_CHANGED" or "DEFENSE_SUPERVISOR_CONFLICT" or WorkflowErrorCodes.EvaluatorRoleConflict)
                        { blockers.Add($"COLD_COMMITTEE_INVALID:{assignment.Id}"); }
                        catch (ConflictException) { blockers.Add($"COLD_EVIDENCE_CHANGED:{assignment.Id}"); }
                        catch (ForbiddenException) { blockers.Add($"COLD_COMMITTEE_INVALID:{assignment.Id}"); }
                    }
                    decimal? score = null;
                    if (assigned.Length == c.RequiredEvaluators && complete.Count == c.RequiredEvaluators)
                    {
                        var weights = c.EvaluatorWeightsJson is null ? null : JsonSerializer.Deserialize<SchemeEvaluatorWeight[]>(c.EvaluatorWeightsJson);
                        var values = complete.Select(f => (Score: VersionedResultCalculator.EvaluationTotal(f!.Scores, policy?.CalculationVersion ?? "LEGACY_V1"),
                            Weight: weights?.SingleOrDefault(w => w.EvaluatorId == f!.EvaluatorId)?.WeightPercent ?? (weights is null ? 100m / c.RequiredEvaluators : 0m))).ToArray();
                        if (values.All(v => v.Weight > 0)) score = decimal.Round(weights is null ? values.Average(v => v.Score)
                            : values.Sum(v => v.Score * v.Weight / 100m), 2, policy?.RoundingMode == "TO_EVEN" ? MidpointRounding.ToEven : MidpointRounding.AwayFromZero);
                    }
                    if (!score.HasValue) blockers.Add($"COLD_GRADING_PENDING:{c.Id}:{student}");
                    var visibleScore = isStudentViewer && !studentResultPublished ? null : score;
                    rows.Add(new(c.Id, c.Name, c.Scope, c.MajorId, student, c.RequiredEvaluators, assigned.Length, complete.Count, visibleScore,
                        c.ProjectWeightPercent > 0 || c.StudentWeightPercent > 0));
                }
            }
            if (rows.Count == 0) blockers.Add("COLD_COMPONENT_REQUIRED");
        }
        return new ColdProgressDto(projectId, package.Status, checklist.Items.Count,
            checklist.Items.Count(i => i.Latest?.Status is "SUBMITTED" or "LOCKED"), checklist.Items.Count(i => i.Latest?.Status == "LOCKED"),
            blockers.Count == 0, blockers, rows);
    }
}
