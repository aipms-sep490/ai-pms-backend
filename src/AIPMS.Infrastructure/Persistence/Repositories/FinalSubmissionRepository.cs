using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.FinalSubmissions.Abstractions;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class FinalSubmissionRepository(AipmsDbContext db) : IFinalSubmissionRepository
{
    public Task<bool> CanManageAsync(long projectId, long actorId, CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == actorId && u.Status == "ACTIVE" &&
            (u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN") ||
             (u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") && u.Department != null
                && u.Department.IsActive && u.Department.Organization.IsActive
                && db.ProjectMajors.Any(m => m.ProjectId == projectId && m.Major.IsActive
                    && m.Major.DepartmentId == u.DepartmentId
                    && m.Major.Department.OrganizationId == m.Project.Team.AcademicSemester.OrganizationId))), ct);

    public async Task<bool> CanReadAsync(long projectId, long actorId, CancellationToken ct)
    {
        if (await CanManageAsync(projectId, actorId, ct)) return true;
        return await db.Users.AnyAsync(u => u.Id == actorId && u.Status == "ACTIVE" && u.Department != null
            && u.Department.IsActive && u.Department.Organization.IsActive
            && db.Projects.Any(p => p.Id == projectId && p.Team.AcademicSemester.OrganizationId == u.Department.OrganizationId
                && ((u.UserRoleUsers.Any(r => r.Role.Code == "STUDENT") && p.Team.TeamMembers.Any(m => m.UserId == actorId && m.LeftAt == null))
                    || (u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER")
                        && p.ProjectMajors.Any(m => m.Major.DepartmentId == u.DepartmentId && m.Major.IsActive)
                        && (db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.IsPrimary && a.EndedAt == null
                                && a.SupervisorProfile.UserId == actorId)
                            || db.Set<EvaluationAssignment>().Any(a => a.ProjectId == projectId && a.EvaluatorId == actorId
                                && a.DepartmentId == u.DepartmentId && a.Status == "ACTIVE"
                                && (a.EvaluationType != "SUPERVISOR" || db.SupervisorAssignments.Any(s => s.ProjectId == projectId
                                    && s.IsPrimary && s.EndedAt == null && s.SupervisorProfile.UserId == actorId))))))), ct);
    }

    public async Task<FinalRequirementsRecord?> RequirementsAsync(long projectId, CancellationToken ct)
    {
        var row = await db.Set<FinalSubmissionRequirements>().AsNoTracking().Include(r => r.Items)
            .SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        return row is null ? null : new(row.ConcurrencyToken.ToString("N"), row.Items.OrderBy(i => i.DeliverableId).Select(i => i.DeliverableId).ToArray());
    }

    public async Task<IReadOnlyList<FinalRequiredDeliverable>> DeliverablesAsync(long projectId, IReadOnlyList<long> ids, CancellationToken ct) =>
        await db.Deliverables.AsNoTracking().Where(d => d.ProjectId == projectId && ids.Contains(d.Id)).OrderBy(d => d.Id)
            .Select(d => new FinalRequiredDeliverable(d.Id, d.Title)).ToListAsync(ct);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Final submission writes require a project transaction.");
    }

    public async Task<FinalRequirementsRecord> ConfigureAsync(long projectId, IReadOnlyList<long> ids, long actorId, DateTime now, CancellationToken ct)
    {
        RequireTransaction();
        var row = await db.Set<FinalSubmissionRequirements>().Include(r => r.Items).SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (row is null)
        {
            row = new() { ProjectId = projectId };
            db.Set<FinalSubmissionRequirements>().Add(row);
        }
        foreach (var item in row.Items.Where(i => !ids.Contains(i.DeliverableId)).ToArray())
        {
            db.Set<FinalSubmissionRequirement>().Remove(item);
            row.Items.Remove(item);
        }
        foreach (var id in ids.Where(id => row.Items.All(i => i.DeliverableId != id))) row.Items.Add(new() { DeliverableId = id });
        row.ConcurrencyToken = Guid.NewGuid();
        row.UpdatedAt = now;
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(ct);
        return new(row.ConcurrencyToken.ToString("N"), ids.Order().ToArray());
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static FinalSubmissionRecord Map(FinalSubmission row) => new(row.Id, row.ProjectId, row.ProjectPeriodId,
        row.SubmittedBy, Utc(row.SubmittedAt), Utc(row.Deadline), row.Notes, row.DraftConcurrencyToken.ToString("N"),
        row.RequirementsConcurrencyToken.ToString("N"), row.Items.OrderBy(i => i.DeliverableVersionId).Select(i =>
            new FinalSnapshotItem(i.DeliverableVersionId, i.DeliverableId, i.Title, i.VersionNumber,
                i.StatusAtSubmission, i.WasRequired, JsonSerializer.Deserialize<FinalSnapshotFile[]>(i.FilesJson)!)).ToArray());

    public async Task<FinalSubmissionRecord?> GetAsync(long projectId, CancellationToken ct)
    {
        var row = await db.Set<FinalSubmission>().AsNoTracking().Include(s => s.Items).SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<FinalSnapshotFile>> FilesAsync(IReadOnlyList<long> versionIds, CancellationToken ct)
    {
        var files = await db.Files.AsNoTracking().Where(f => f.DeliverableVersionId.HasValue
            && versionIds.Contains(f.DeliverableVersionId.Value)).OrderBy(f => f.Id).ToListAsync(ct);
        return files.Select(f => new FinalSnapshotFile(f.ToDto() with { CreatedAt = Utc(f.CreatedAt) }, f.StoragePath)).ToArray();
    }

    public async Task<FinalSubmissionRecord> SubmitAsync(FinalSubmissionRecord input, CancellationToken ct)
    {
        RequireTransaction();
        var changed = await db.Projects.Where(p => p.Id == input.ProjectId && p.Status == "ACTIVE")
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "FINAL_SUBMISSION").SetProperty(p => p.UpdatedAt, input.SubmittedAt), ct);
        if (changed != 1) throw new ConflictException("PROJECT_NOT_ACTIVE");
        var row = new FinalSubmission { ProjectId = input.ProjectId, ProjectPeriodId = input.ProjectPeriodId,
            SubmittedBy = input.SubmittedBy, SubmittedAt = input.SubmittedAt, Deadline = input.Deadline, Notes = input.Notes,
            DraftConcurrencyToken = Guid.Parse(input.DraftConcurrencyToken), RequirementsConcurrencyToken = Guid.Parse(input.RequirementsConcurrencyToken),
            Items = input.Items.Select(i => new FinalSubmissionItem { DeliverableVersionId = i.DeliverableVersionId,
                DeliverableId = i.DeliverableId, Title = i.Title, VersionNumber = i.VersionNumber, StatusAtSubmission = i.StatusAtSubmission,
                WasRequired = i.WasRequired, FilesJson = JsonSerializer.Serialize(i.Files) }).ToArray() };
        db.Set<FinalSubmission>().Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }
}
