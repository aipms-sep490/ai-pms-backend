using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class PublishedResultResolver
{
    internal static async Task<ResultRevision?> Revision(AipmsDbContext db, long projectId, long target, CancellationToken ct) =>
        await db.Set<ResultRevision>().AsNoTracking().Where(r => r.ProjectId == projectId && r.TargetKey == target)
            .OrderByDescending(r => r.Version).FirstOrDefaultAsync(ct);
    internal static ProjectResultDto Root(ProjectResult row) => JsonSerializer.Deserialize<ProjectResultDto>(row.SnapshotJson)!
        with { Id = row.Id, Version = 1, RevisionId = null, SupersedesVersion = null };
    internal static StudentResultDto Root(StudentResult row) => new(row.Id, row.ProjectId, row.StudentId, row.MajorId, row.SchemeId,
        row.TotalScore, row.PassThreshold, row.Outcome, row.CalculationRule, row.PublishedBy, DateTime.SpecifyKind(row.PublishedAt, DateTimeKind.Utc),
        row.SnapshotJson, ResultSnapshotReader.Read(row.SnapshotJson, row.CalculationRule, row.StudentId, row.MajorId));
    internal static ProjectResultDto Project(ResultRevision row) => JsonSerializer.Deserialize<ProjectResultDto>(row.SnapshotJson)!
        with { Version = row.Version, RevisionId = row.Id, SupersedesVersion = row.Version - 1 };
    internal static StudentResultDto Student(ResultRevision row) => JsonSerializer.Deserialize<StudentResultDto>(row.SnapshotJson)!
        with { Version = row.Version, RevisionId = row.Id, SupersedesVersion = row.Version - 1 };
    internal static async Task<ProjectResultDto?> Project(AipmsDbContext db, long projectId, CancellationToken ct)
    {
        var root = await db.Set<ProjectResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (root is null) return null;
        var latest = await Revision(db, projectId, 0, ct);
        return latest is null ? Root(root) : Project(latest);
    }
    internal static async Task<StudentResultDto?> Student(AipmsDbContext db, long projectId, long studentId, CancellationToken ct)
    {
        var root = await db.Set<StudentResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct);
        if (root is null) return null;
        var latest = await Revision(db, projectId, studentId, ct);
        return latest is null ? Root(root) : Student(latest);
    }
}
