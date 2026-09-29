using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record ProjectAcademicScope(long? LeadDepartmentId, IReadOnlyList<long> DepartmentIds,
    IReadOnlyList<long> MajorIds, IReadOnlyDictionary<long, long>? MajorDepartmentIds = null);

internal static class ProjectAcademicScopeReader
{
    public static async Task<ProjectAcademicScope> ReadAsync(AipmsDbContext context, long projectId, CancellationToken ct)
    {
        var project = await context.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.TeamId, p.Status }).SingleOrDefaultAsync(ct);
        if (project is null) return new(null, [], []);
        if (project.Status is not ("DRAFT" or "REVISION_REQUIRED"))
        {
            var json = await context.Set<ProjectRegistrationSnapshot>().AsNoTracking()
                .Where(s => s.ProjectId == projectId).OrderByDescending(s => s.Id)
                .Select(s => s.SnapshotJson).FirstOrDefaultAsync(ct);
            if (json is not null)
            {
                var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(json)!;
                return new(evidence.Scope.LeadDepartmentId, evidence.DepartmentIds,
                    evidence.Scope.Requirements.Select(r => r.MajorId).ToArray(), evidence.MajorDepartmentIds);
            }
        }
        var majors = await context.ProjectMajors.AsNoTracking().Where(m => m.ProjectId == projectId)
            .Select(m => new { m.MajorId, m.Major.DepartmentId }).ToListAsync(ct);
        var lead = await context.Set<TeamAcademicConfiguration>().AsNoTracking()
            .Where(c => c.TeamId == project.TeamId).Select(c => (long?)c.LeadDepartmentId).SingleOrDefaultAsync(ct);
        var departments = majors.Select(m => m.DepartmentId).Distinct().ToArray();
        return new(lead ?? (departments.Length == 1 ? departments[0] : null), departments,
            majors.Select(m => m.MajorId).ToArray(), majors.ToDictionary(m => m.MajorId, m => m.DepartmentId));
    }
}
