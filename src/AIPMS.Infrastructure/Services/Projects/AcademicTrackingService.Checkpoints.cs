using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class AcademicTrackingService
{
    private CheckpointDto CheckpointDto(ProjectCheckpoint row, Access access) => new(row.Id, row.ProjectId, row.Title, row.MajorId,
        Utc(row.PlannedAt), row.ConcurrencyToken.ToString("N"), row.PlannedAt < Now && row.Decisions.OrderByDescending(d => d.Id).FirstOrDefault()?.Decision is not ("MET" or "CANCELLED"),
        access.Writable && access.CanManage(row.MajorId), row.Decisions.OrderBy(d => d.Id).Select(d => new CheckpointDecisionDto(d.Id,
            d.Decision, d.Reason, d.DecidedBy, Utc(d.DecidedAt), JsonSerializer.Deserialize<CheckpointEvidenceInput[]>(d.EvidenceJson)!)).ToArray());
    public Task<CheckpointDto> SaveCheckpoint(long projectId, long? id, SaveCheckpointRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct); Write(access, input.MajorId);
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 200 || input.PlannedAt.Year is < 2000 or > 2200) throw Invalid("A title and valid planned date are required.");
        ProjectCheckpoint row;
        if (id.HasValue)
        {
            row = await db.Set<ProjectCheckpoint>().Include(c => c.Decisions).SingleOrDefaultAsync(c => c.Id == id && c.ProjectId == projectId, ct)
                ?? throw new NotFoundException("Checkpoint", id.Value);
            Write(access, row.MajorId); CheckToken(row.ConcurrencyToken, input.ConcurrencyToken ?? "");
            if (row.MajorId != input.MajorId) throw new ConflictException("Checkpoint scope is immutable.");
            if (row.Decisions.Count > 0) throw new ConflictException("A decided checkpoint retains its original plan; create a new checkpoint.");
        }
        else
        {
            if (input.ConcurrencyToken is not null) throw Invalid("New checkpoints do not accept a token.");
            row = new() { ProjectId = projectId, MajorId = input.MajorId, CreatedBy = Actor, CreatedAt = Now }; db.Add(row);
        }
        row.Title = input.Title.Trim(); row.PlannedAt = input.PlannedAt.UtcDateTime; row.ConcurrencyToken = Guid.NewGuid(); row.UpdatedAt = Now;
        await db.SaveChangesAsync(ct); await Audit("CHECKPOINT_PLANNED", "PROJECT_CHECKPOINT", row.Id,
            new { projectId, row.MajorId, row.PlannedAt, row.ConcurrencyToken }, ct);
        return CheckpointDto(row, access);
    }, ct);
    public Task<CheckpointDto> DecideCheckpoint(long id, DecideCheckpointRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        var projectId = await db.Set<ProjectCheckpoint>().Where(c => c.Id == id).Select(c => (long?)c.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Checkpoint", id);
        await transactions.LockProjectAsync(projectId, ct); var access = await Scope(projectId, ct);
        var row = await db.Set<ProjectCheckpoint>().Include(c => c.Decisions).SingleAsync(c => c.Id == id, ct);
        Write(access, row.MajorId); CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (input.Decision is not ("MET" or "AT_RISK" or "NOT_MET" or "CANCELLED") || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000
            || input.Evidence is null || input.Evidence.Count > 50 || input.Evidence.Any(e => e is null || e.EvidenceId <= 0 || e.ReviewId <= 0)
            || input.Evidence.Select(e => e.EvidenceId).Distinct().Count() != input.Evidence.Count) throw Invalid("Invalid checkpoint decision or bounded reviewed evidence list.");
        if (row.Decisions.Count >= 100) throw new ConflictException("Checkpoint history reached its limit; create a successor checkpoint.");
        var evidenceIds = input.Evidence.Select(e => e.EvidenceId).ToArray();
        var records = await db.ProjectEvidence.Where(e => evidenceIds.Contains(e.Id) && e.ProjectId == projectId).ToArrayAsync(ct);
        var reviewIds = input.Evidence.Select(e => e.ReviewId).ToArray();
        var reviews = await db.Set<ProjectEvidenceReview>().Where(r => reviewIds.Contains(r.Id)).ToArrayAsync(ct);
        if (records.Length != evidenceIds.Length || input.Evidence.Any(e => !reviews.Any(r => r.Id == e.ReviewId && r.EvidenceId == e.EvidenceId))
            || records.Any(e => !access.CanRead(e.MajorId) || row.MajorId.HasValue && row.MajorId != e.MajorId))
            throw new ForbiddenException("Checkpoint evidence must reference a scoped review in the same project.");
        row.Decisions.Add(new() { Decision = input.Decision, Reason = input.Reason.Trim(), EvidenceJson = JsonSerializer.Serialize(input.Evidence),
            DecidedBy = Actor, DecidedAt = Now });
        row.ConcurrencyToken = Guid.NewGuid(); row.UpdatedAt = Now;
        await db.SaveChangesAsync(ct); await Audit("CHECKPOINT_DECISION_RECORDED", "PROJECT_CHECKPOINT", id,
            new { projectId, input.Decision, row.ConcurrencyToken }, ct);
        return CheckpointDto(row, access);
    }, ct);
    public Task<PagedResult<CheckpointDto>> Checkpoints(long projectId, int page, int pageSize, CancellationToken ct) => transactions.InTransactionAsync(async () =>
    {
        Page(page, pageSize); var access = await Scope(projectId, ct);
        var majors = access.Registration.Scope.Requirements.Select(r => r.MajorId).Where(m => access.CanRead(m)).ToArray();
        var common = access.CanRead(null);
        var query = db.Set<ProjectCheckpoint>().AsNoTracking().Where(c => c.ProjectId == projectId
            && (c.MajorId == null && common || c.MajorId.HasValue && majors.Contains(c.MajorId.Value)));
        var count = await query.LongCountAsync(ct);
        var rows = await query.Include(c => c.Decisions).OrderBy(c => c.PlannedAt).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new PagedResult<CheckpointDto>(rows.Select(c => CheckpointDto(c, access)).ToArray(), page, pageSize, count);
    }, ct);
}
