using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Evaluation = AIPMS.Infrastructure.Persistence.Generated.Models.Evaluation;
using EvaluationDetail = AIPMS.Infrastructure.Persistence.Generated.Models.EvaluationDetail;
using Rubric = AIPMS.Infrastructure.Persistence.Generated.Models.Rubric;
using RubricCriterion = AIPMS.Infrastructure.Persistence.Generated.Models.RubricCriterion;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class EvaluationRepository(AipmsDbContext db) : IEvaluationRepository
{
    public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) =>
        db.Projects.AnyAsync(x => x.Id == projectId, ct);

    public Task<bool> CanEvaluateProjectAsync(long userId, long projectId, CancellationToken ct) =>
        db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null &&
            x.SupervisorProfile.UserId == userId, ct);

    public async Task<RubricDto?> GetRubricAsync(long id, CancellationToken ct)
    {
        var rubric = await db.Rubrics.AsNoTracking().Include(x => x.RubricCriteria)
            .ThenInclude(x => x.Criterion).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rubric is null) return null;
        return rubric.ToDto(new RubricVersionMetadata { RubricId = rubric.Id, VersionNumber = rubric.VersionNumber, ApprovalStatus = rubric.ApprovalStatus, ApprovedBy = rubric.ApprovedBy, ApprovedAt = rubric.ApprovedAt });
    }

    public async Task<PagedResult<RubricDto>> ListRubricsAsync(bool? active, int page, int size, CancellationToken ct)
    {
        page = Math.Max(1, page); size = Math.Clamp(size, 1, 100);
        var query = db.Rubrics.AsNoTracking().Include(x => x.RubricCriteria).ThenInclude(x => x.Criterion).AsQueryable();
        if (active.HasValue) query = query.Where(x => x.IsActive == active.Value);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(x => x.Code).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var metadata = rows.ToDictionary(x => x.Id, x => new RubricVersionMetadata { RubricId = x.Id, VersionNumber = x.VersionNumber, ApprovalStatus = x.ApprovalStatus, ApprovedBy = x.ApprovedBy, ApprovedAt = x.ApprovedAt });
        return new(rows.Select(x => x.ToDto(metadata.GetValueOrDefault(x.Id))).ToList(), page, size, total);
    }

    public async Task<long> CreateRubricAsync(CreateRubricData data, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rubric = new Rubric { DepartmentId = data.DepartmentId, AcademicSemesterId = data.AcademicSemesterId,
            Code = data.Code, Name = data.Name, Description = data.Description, VersionNumber = data.VersionNumber,
            ApprovalStatus = "DRAFT", IsActive = false, CreatedBy = data.ActorId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Rubrics.Add(rubric); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return rubric.Id;
    }

    public async Task<bool> UpdateRubricAsync(long id, UpdateRubricData data, CancellationToken ct) =>
        await DraftRubrics(id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, data.Name)
            .SetProperty(x => x.Description, data.Description).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) == 1;

    public async Task<bool> DeleteRubricAsync(long id, CancellationToken ct)
    {
        if (await db.Evaluations.AnyAsync(x => x.RubricId == id, ct)) return false;
        return await DraftRubrics(id).ExecuteDeleteAsync(ct) == 1;
    }

    public async Task<bool> UpsertRubricCriterionAsync(long rubricId, UpsertRubricCriterionData data, CancellationToken ct)
    {
        if (!await DraftRubrics(rubricId).AnyAsync(ct) ||
            !await db.EvaluationCriteria.AnyAsync(x => x.Id == data.CriterionId && x.IsActive, ct)) return false;
        var row = await db.RubricCriteria.SingleOrDefaultAsync(x => x.RubricId == rubricId && x.CriterionId == data.CriterionId, ct);
        if (row is null) db.RubricCriteria.Add(new RubricCriterion { RubricId = rubricId,
            CriterionId = data.CriterionId, WeightPercent = data.WeightPercent, MaxScore = data.MaxScore,
            SortOrder = data.SortOrder, IsRequired = data.IsRequired });
        else { row.WeightPercent = data.WeightPercent; row.MaxScore = data.MaxScore; row.SortOrder = data.SortOrder; row.IsRequired = data.IsRequired; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct); return true;
    }

    public async Task<bool> DeleteRubricCriterionAsync(long rubricId, long rubricCriterionId, CancellationToken ct)
    {
        if (!await DraftRubrics(rubricId).AnyAsync(ct)) return false;
        return await db.RubricCriteria.Where(x => x.Id == rubricCriterionId && x.RubricId == rubricId).ExecuteDeleteAsync(ct) == 1;
    }

    public async Task<bool> ReorderRubricCriteriaAsync(long rubricId, IReadOnlyList<long> orderedIds, CancellationToken ct)
    {
        if (!await DraftRubrics(rubricId).AnyAsync(ct)) return false;
        var rows = await db.RubricCriteria.Where(x => x.RubricId == rubricId).ToListAsync(ct);
        if (rows.Count != orderedIds.Count || rows.Select(x => x.Id).Except(orderedIds).Any()) return false;
        for (var i = 0; i < orderedIds.Count; i++) rows.Single(x => x.Id == orderedIds[i]).SortOrder = i;
        await db.SaveChangesAsync(ct); return true;
    }

    public async Task<bool> SetRubricActiveAsync(long rubricId, bool active, long actorId, DateTime at, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (active)
        {
            var updated = await DraftRubrics(rubricId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true).SetProperty(x => x.ApprovalStatus, "APPROVED").SetProperty(x => x.ApprovedBy, (long?)actorId).SetProperty(x => x.ApprovedAt, (DateTime?)at).SetProperty(x => x.UpdatedAt, at), ct);
            if (updated != 1) return false;
        }
        else
        {
            if (await db.Rubrics.Where(x => x.Id == rubricId && x.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, at), ct) != 1) return false;
        }
        await tx.CommitAsync(ct); return true;
    }

    public async Task<long> CreateDraftAsync(long projectId, long evaluatorId, long rubricId,
        string evaluationType, string? evidenceSummary, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var validRubric = await db.Rubrics.AnyAsync(x => x.Id == rubricId && x.IsActive && x.ApprovalStatus == "APPROVED", ct);
        if (!validRubric) throw new InvalidOperationException("Rubric version is not approved.");
        var evaluation = new Evaluation { ProjectId = projectId, EvaluatorId = evaluatorId,
            RubricId = rubricId, EvaluationType = evaluationType, Status = "DRAFT" };
        evaluation.EvidenceSummary = evidenceSummary; evaluation.RoundingRule = "AWAY_FROM_ZERO_2DP";
        db.Evaluations.Add(evaluation); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return evaluation.Id;
    }

    public async Task<EvaluationDetailDto?> GetAsync(long id, CancellationToken ct)
    {
        var evaluation = await db.Evaluations.AsNoTracking().Include(x => x.EvaluationDetails)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (evaluation is null) return null;
        var criteria = await db.RubricCriteria.AsNoTracking().Include(x => x.Criterion)
            .Where(x => x.RubricId == evaluation.RubricId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var rubric = await db.Rubrics.AsNoTracking().SingleAsync(x => x.Id == evaluation.RubricId, ct);
        var metadata = new RubricVersionMetadata { RubricId = rubric.Id, VersionNumber = rubric.VersionNumber, ApprovalStatus = rubric.ApprovalStatus, ApprovedBy = rubric.ApprovedBy, ApprovedAt = rubric.ApprovedAt };
        var audit = new EvaluationAudit { EvaluationId = id, EvidenceSummary = evaluation.EvidenceSummary, FinalizedBy = evaluation.FinalizedBy, FinalizedAt = evaluation.FinalizedAt, RoundingRule = evaluation.RoundingRule };
        var scores = criteria.Select(c => { var detail = evaluation.EvaluationDetails.SingleOrDefault(x => x.RubricCriterionId == c.Id);
            return new EvaluationScoreDto(c.Id, c.CriterionId, c.Criterion.Code, c.Criterion.Name,
                c.WeightPercent, c.MaxScore, c.IsRequired, c.SortOrder, detail?.Score, detail?.Comments); }).ToList();
        return new(evaluation.ToDto(metadata, audit), scores);
    }

    public async Task<PagedResult<EvaluationDto>> GetByProjectAsync(long projectId, int page, int size, CancellationToken ct)
    {
        page = Math.Max(1, page); size = Math.Clamp(size, 1, 100);
        var query = db.Evaluations.AsNoTracking().Where(x => x.ProjectId == projectId).OrderByDescending(x => x.Id);
        var total = await query.LongCountAsync(ct); var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var rubricIds = rows.Select(x => x.RubricId).Distinct().ToArray(); var evaluationIds = rows.Select(x => x.Id).ToArray();
        var rubricRows = await db.Rubrics.AsNoTracking().Where(x => rubricIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var metadata = rubricRows.ToDictionary(x => x.Key, x => new RubricVersionMetadata { RubricId = x.Key, VersionNumber = x.Value.VersionNumber, ApprovalStatus = x.Value.ApprovalStatus, ApprovedBy = x.Value.ApprovedBy, ApprovedAt = x.Value.ApprovedAt });
        var audits = rows.ToDictionary(x => x.Id, x => new EvaluationAudit { EvaluationId = x.Id, EvidenceSummary = x.EvidenceSummary, FinalizedBy = x.FinalizedBy, FinalizedAt = x.FinalizedAt, RoundingRule = x.RoundingRule });
        return new(rows.Select(x => x.ToDto(metadata.GetValueOrDefault(x.RubricId), audits.GetValueOrDefault(x.Id))).ToList(), page, size, total);
    }

    public async Task<bool> UpsertScoreAsync(long evaluationId, long rubricCriterionId, decimal score, string? comments, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (!await db.Evaluations.AnyAsync(x => x.Id == evaluationId && x.Status == "DRAFT", ct))
        { await tx.RollbackAsync(ct); return false; }
        var detail = await db.EvaluationDetails.SingleOrDefaultAsync(x => x.EvaluationId == evaluationId && x.RubricCriterionId == rubricCriterionId, ct);
        if (detail is null) db.EvaluationDetails.Add(new EvaluationDetail { EvaluationId = evaluationId, RubricCriterionId = rubricCriterionId, Score = score, Comments = comments });
        else { detail.Score = score; detail.Comments = comments; detail.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }

    public async Task<bool> UpdateCommentsAsync(long id, string? comments, string? evidenceSummary, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await db.Evaluations.Where(x => x.Id == id && x.Status == "DRAFT").ExecuteUpdateAsync(s => s.SetProperty(x => x.Comments, comments).SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct) != 1)
        { await tx.RollbackAsync(ct); return false; }

        await tx.CommitAsync(ct); return true;
    }

    public async Task<bool> FinalizeAsync(long id, decimal total, long actorId, DateTime at, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var affected = await db.Evaluations.Where(x => x.Id == id && x.Status == "DRAFT")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "FINALIZED")
                .SetProperty(x => x.TotalScore, total).SetProperty(x => x.EvaluatedAt, at)
                .SetProperty(x => x.FinalizedBy, (long?)actorId).SetProperty(x => x.FinalizedAt, (DateTime?)at)
                .SetProperty(x => x.UpdatedAt, at), ct);
        if (affected != 1) { await tx.RollbackAsync(ct); return false; }
        await tx.CommitAsync(ct); return true;
    }

    private IQueryable<Rubric> DraftRubrics(long id) => db.Rubrics.Where(x => x.Id == id && x.ApprovalStatus == "DRAFT");
}


