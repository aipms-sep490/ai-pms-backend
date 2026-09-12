using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class FinalSubmissionDraftRepository(AipmsDbContext db) : IFinalSubmissionDraftRepository
{
    public async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var result = await action();
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            throw new ConflictException("The draft, project, selected versions or submission period changed. Reload and retry.");
        }
    }

    private static bool IsConflict(Exception ex)
    {
        for (Exception? cause = ex; cause is not null; cause = cause.InnerException)
            if (cause is DbUpdateConcurrencyException || cause is SqlException { Number: 1205 or 1222 or 2601 or 2627 or 547 }) return true;
        return false;
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Final draft writes require a transaction.");
    }

    public async Task LockProjectAsync(long projectId, CancellationToken ct)
    {
        RequireTransaction();
        var found = await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.projects WITH (XLOCK, HOLDLOCK) WHERE id = {projectId}").ToListAsync(ct);
        if (found.Count == 0) throw new NotFoundException("Project", projectId);
    }

    public Task<FinalDraftProject?> GetProjectAsync(long projectId, long actorId, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new FinalDraftProject(p.Id,
            p.Team.AcademicSemesterId, p.Status,
            p.Team.AcademicSemester.Organization.IsActive && p.ProjectMajors.Any()
                && p.ProjectMajors.All(m => m.Major.IsActive && m.Major.Department.IsActive
                    && m.Major.Department.Organization.IsActive
                    && m.Major.Department.OrganizationId == p.Team.AcademicSemester.OrganizationId),
            p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null),
            p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null && m.IsLeader)))
            .SingleOrDefaultAsync(ct);

    private static IQueryable<FinalDraftPeriod> ProjectPeriods(IQueryable<Generated.Models.ProjectPeriod> query, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        return query.Select(p => new FinalDraftPeriod(
            p.Id, p.AcademicSemesterId, p.Name, p.PeriodType, p.Status, p.StartAt, p.EndAt,
            p.AcademicSemester.Status == "ACTIVE" && p.AcademicSemester.StartDate <= today
                && today <= p.AcademicSemester.EndDate && p.AcademicSemester.Organization.IsActive));
    }

    public async Task<FinalDraftPeriod?> GetPeriodAsync(long periodId, DateTime now, CancellationToken ct)
    {
        var row = await ProjectPeriods(db.ProjectPeriods.AsNoTracking().Where(p => p.Id == periodId), now).SingleOrDefaultAsync(ct);
        return row is null ? null : row with { StartAt = Utc(row.StartAt), EndAt = Utc(row.EndAt) };
    }

    public async Task<PagedResult<FinalDraftPeriod>> GetPeriodsAsync(long semesterId, DateTime now, int page, int pageSize, CancellationToken ct)
    {
        var query = db.ProjectPeriods.AsNoTracking().Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == "FINAL_SUBMISSION");
        var total = await query.LongCountAsync(ct);
        var rows = await ProjectPeriods(query.OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize), now).ToListAsync(ct);
        return new(rows.Select(p => p with { StartAt = Utc(p.StartAt), EndAt = Utc(p.EndAt) }).ToArray(), page, pageSize, total);
    }

    public Task<int> CountOpenPeriodsAsync(long semesterId, DateTime now, CancellationToken ct) =>
        db.ProjectPeriods.CountAsync(p => p.AcademicSemesterId == semesterId && p.PeriodType == "FINAL_SUBMISSION"
            && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt, ct);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static FinalDraftRecord Map(FinalSubmissionDraft row) => new(row.Id, row.ProjectId, row.ProjectPeriodId,
        row.Notes, row.CreatedBy, row.UpdatedBy, Utc(row.CreatedAt), Utc(row.UpdatedAt), row.ConcurrencyToken.ToString("N"),
        row.Items.OrderBy(i => i.DeliverableVersionId).Select(i => i.DeliverableVersionId).ToArray());

    public async Task<FinalDraftRecord?> GetAsync(long projectId, CancellationToken ct)
    {
        var row = await db.Set<FinalSubmissionDraft>().AsNoTracking().Include(d => d.Items)
            .SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<FinalDraftVersion>> GetVersionsAsync(long projectId, IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var rows = await db.DeliverableVersions.AsNoTracking().Include(v => v.Deliverable).Include(v => v.Files)
            .Where(v => ids.Contains(v.Id) && v.Deliverable.ProjectId == projectId).OrderBy(v => v.Id).ToListAsync(ct);
        return rows.Select(v => new FinalDraftVersion(v.Id, v.Deliverable.ProjectId, v.DeliverableId,
            v.Deliverable.Title, v.VersionNumber, v.Status,
            v.Files.Count > 0 && v.Files.All(f => f.ProgressReportId is null && f.MeetingId is null && f.SupervisorFeedbackId is null
                && f.FileSizeBytes > 0 && !string.IsNullOrWhiteSpace(f.OriginalFileName) && !string.IsNullOrWhiteSpace(f.StoragePath)
                && f.ChecksumSha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit)),
            v.Files.OrderBy(f => f.Id).Select(f => f.ToDto() with { CreatedAt = Utc(f.CreatedAt) }).ToArray())).ToArray();
    }

    public async Task<FinalDraftRecord> SaveAsync(long projectId, long periodId, string? notes,
        IReadOnlyList<long> versionIds, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var row = await db.Set<FinalSubmissionDraft>().Include(d => d.Items).SingleOrDefaultAsync(d => d.ProjectId == projectId, ct);
        if (row is null)
        {
            row = new FinalSubmissionDraft { ProjectId = projectId, CreatedBy = actorId, CreatedAt = now };
            db.Set<FinalSubmissionDraft>().Add(row);
        }
        row.ProjectPeriodId = periodId;
        row.Notes = notes;
        row.UpdatedBy = actorId;
        row.UpdatedAt = now;
        row.ConcurrencyToken = Guid.NewGuid();
        foreach (var item in row.Items.Where(i => !versionIds.Contains(i.DeliverableVersionId)).ToArray())
        {
            db.Set<FinalSubmissionDraftItem>().Remove(item);
            row.Items.Remove(item);
        }
        foreach (var versionId in versionIds.Where(id => row.Items.All(i => i.DeliverableVersionId != id)))
            row.Items.Add(new() { DeliverableVersionId = versionId });
        await db.SaveChangesAsync(ct);
        return Map(row);
    }
}
