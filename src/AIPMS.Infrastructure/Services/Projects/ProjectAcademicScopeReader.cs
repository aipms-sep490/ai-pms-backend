using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed record ProjectAcademicScope(long? LeadDepartmentId, IReadOnlyList<long> DepartmentIds,
    IReadOnlyList<long> MajorIds, IReadOnlyDictionary<long, long>? MajorDepartmentIds = null,
    string Provenance = "UNKNOWN");

internal static class ProjectAcademicScopeReader
{
    public static RegistrationEvidence? ReadHistoricalEvidence(string json)
    {
        try { return JsonSerializer.Deserialize<RegistrationEvidence>(json); }
        catch (JsonException) { return null; }
    }

    public static RegistrationEvidence? ParseEvidence(string json)
    {
        try
        {
            var evidence = JsonSerializer.Deserialize<RegistrationEvidence>(json);
            if (evidence?.Scope is not { LeadDepartmentId: > 0, Requirements.Count: > 0 } scope
                || evidence.DepartmentIds is not { Count: > 0 }
                || !evidence.DepartmentIds.Contains(scope.LeadDepartmentId)
                || evidence.DepartmentIds.Any(id => id <= 0)
                || evidence.DepartmentIds.Distinct().Count() != evidence.DepartmentIds.Count
                || scope.ProjectMode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")
                || scope.Requirements.Any(r => r is null || r.MajorId <= 0)
                || scope.Requirements.Select(r => r.MajorId).Distinct().Count() != scope.Requirements.Count
                || evidence.MajorDepartmentIds is null
                || evidence.MajorDepartmentIds.Count != scope.Requirements.Count
                || !evidence.DepartmentIds.Order().SequenceEqual(evidence.MajorDepartmentIds.Values.Distinct().Order())
                || scope.Requirements.Any(r => !evidence.MajorDepartmentIds.TryGetValue(r.MajorId, out var department)
                    || !evidence.DepartmentIds.Contains(department))) return null;
            return evidence;
        }
        catch (JsonException) { return null; }
    }

    public static async Task<ProjectAcademicScope> ReadAsync(AipmsDbContext context, long projectId, CancellationToken ct,
        bool requireFrozenScope = false)
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
                var evidence = ParseEvidence(json);
                if (evidence is not null)
                    return new(evidence.Scope.LeadDepartmentId, evidence.DepartmentIds,
                        evidence.Scope.Requirements.Select(r => r.MajorId).Distinct().ToArray(), evidence.MajorDepartmentIds,
                        "FROZEN_REGISTRATION_SNAPSHOT");
                return new(null, [], []);
            }
            if (requireFrozenScope) return new(null, [], []);
        }
        var majors = await context.ProjectMajors.AsNoTracking().Where(m => m.ProjectId == projectId)
            .Select(m => new { m.MajorId, m.Major.DepartmentId }).ToListAsync(ct);
        var fallbackLead = await context.Set<TeamAcademicConfiguration>().AsNoTracking()
            .Where(c => c.TeamId == project.TeamId).Select(c => (long?)c.LeadDepartmentId).SingleOrDefaultAsync(ct);
        var departments = majors.Select(m => m.DepartmentId).Distinct().ToArray();
        return new(fallbackLead ?? (departments.Length == 1 ? departments[0] : null), departments,
            majors.Select(m => m.MajorId).ToArray(), majors.ToDictionary(m => m.MajorId, m => m.DepartmentId), "CURRENT_CONFIGURATION");
    }
}
