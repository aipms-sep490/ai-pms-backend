using System.Text.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests;

internal static class AcademicSnapshotFixture
{
    public static async Task AddAsync(AipmsDbContext db, long projectId, long periodId, long leadDepartment, long actorId, DateTime now)
    {
        var departments = await db.ProjectMajors.Where(m => m.ProjectId == projectId)
            .Select(m => new { m.MajorId, m.Major.DepartmentId }).ToDictionaryAsync(m => m.MajorId, m => m.DepartmentId);
        var project = await db.Projects.Include(p => p.Team).SingleAsync(p => p.Id == projectId);
        var organization = await db.AcademicSemesters.Where(s => s.Id == project.Team.AcademicSemesterId).Select(s => s.OrganizationId).SingleAsync();
        var members = await db.TeamMembers.Where(m => m.TeamId == project.TeamId && m.LeftAt == null && m.User.MajorId != null)
            .Select(m => new RegisteredMemberDto(m.UserId, m.User.FullName, m.User.MajorId!.Value, m.IsLeader)).ToArrayAsync();
        var scope = new TeamAcademicScopeDto(departments.Count == 1 ? "SINGLE_MAJOR" : "INTERDISCIPLINARY",
            departments.Count == 1 ? departments.Keys.Single() : null, leadDepartment,
            departments.Keys.Select(id => new MajorRequirementDto(id, 0, 5, "Fixture responsibility")).ToArray(), Guid.NewGuid());
        db.Add(new ProjectRegistrationSnapshot { ProjectId = projectId, ProjectPeriodId = periodId,
            LeadDepartmentId = leadDepartment, SubmittedBy = actorId, SubmittedAt = now,
            SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(scope, new(1, 5, 1, "fixture"), organization,
                now.AddDays(-1), now.AddDays(1), members, departments.Values.Distinct().ToArray(), MajorDepartmentIds: departments)) });
        await db.SaveChangesAsync();
    }
}
