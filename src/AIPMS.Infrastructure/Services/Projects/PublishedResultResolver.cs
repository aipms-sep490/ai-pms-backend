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
    internal static async Task<ProjectResultDto?> Project(AipmsDbContext db, long projectId, CancellationToken ct, bool enrich = true)
    {
        var root = await db.Set<ProjectResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId, ct);
        if (root is null) return null;
        var latest = await Revision(db, projectId, 0, ct);
        var dto = latest is null ? Root(root) : Project(latest);
        return enrich ? await EnrichAsync(db, dto, ct) : dto;
    }
    internal static async Task<StudentResultDto?> Student(AipmsDbContext db, long projectId, long studentId, CancellationToken ct)
    {
        var root = await db.Set<StudentResult>().AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId && r.StudentId == studentId, ct);
        if (root is null) return null;
        var latest = await Revision(db, projectId, studentId, ct);
        var dto = latest is null ? Root(root) : Student(latest);
        if (dto.Breakdown is not null)
            dto = dto with { Breakdown = await EnrichBreakdownAsync(db, dto.Breakdown, ct) };
        return dto;
    }
    internal static async Task<ProjectResultDto> EnrichAsync(AipmsDbContext db, ProjectResultDto dto, CancellationToken ct)
    {
        var contributions = dto.Contributions;
        if (contributions.Any(c => c.EvaluatorName is null))
        {
            var evalIds = contributions.Select(c => c.EvaluatorId).Distinct().ToArray();
            var names = await db.Users.AsNoTracking().Where(u => evalIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            contributions = contributions.Select(c => c.EvaluatorName is null && names.TryGetValue(c.EvaluatorId, out var name)
                ? c with { EvaluatorName = name } : c).ToArray();
        }
        var breakdown = dto.Breakdown is not null ? await EnrichBreakdownAsync(db, dto.Breakdown, ct) : null;
        return dto with { Contributions = contributions, Breakdown = breakdown };
    }
    internal static async Task<ResultBreakdownDto> EnrichBreakdownAsync(AipmsDbContext db, ResultBreakdownDto breakdown, CancellationToken ct)
    {
        if (breakdown.MajorBreakdown is null || !breakdown.MajorBreakdown.Any(m => m.MajorName is null))
            return breakdown;
        var majorIds = breakdown.MajorBreakdown.Select(m => m.MajorId).Distinct().ToArray();
        if (majorIds.Length == 0) return breakdown;
        var names = await db.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        return breakdown with
        {
            MajorBreakdown = breakdown.MajorBreakdown.Select(m => m.MajorName is null && names.TryGetValue(m.MajorId, out var name)
                ? m with { MajorName = name } : m).ToArray()
        };
    }
}
