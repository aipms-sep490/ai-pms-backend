using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    private async Task<AssignEvaluatorRequest> ScopedRequest(HttpClient staff, EvaluationScenario s, long? user = null, string type = "LECTURER", bool single = false,
        decimal threshold = 5, decimal? firstWeight = null)
    {
        await using var db = database.CreateContext();
        var published = await db.Set<EvaluationScheme>().Include(x => x.Components).SingleOrDefaultAsync(x => x.ProjectId == s.ProjectId && x.Status == "PUBLISHED");
        if (published is null)
        {
            var project = await db.Projects.Include(p => p.Team).Include(p => p.ProjectMajors).SingleAsync(p => p.Id == s.ProjectId);
            var major = project.ProjectMajors.Single().MajorId;
            var student = await db.Users.SingleAsync(u => u.Id == s.Scope.Users.Student);
            student.MajorId = major; student.AcademicProfileStatus = "VERIFIED";
            if (!await db.TeamMembers.AnyAsync(m => m.TeamId == project.TeamId && m.UserId == student.Id))
                db.TeamMembers.Add(new() { TeamId = project.TeamId, AcademicSemesterId = s.Scope.SemesterId, UserId = student.Id, IsLeader = true });
            if (!await db.Set<ProjectRegistrationSnapshot>().AnyAsync(x => x.ProjectId == s.ProjectId))
                db.Add(new ProjectRegistrationSnapshot { ProjectId = project.Id, ProjectPeriodId = s.PeriodId,
                    LeadDepartmentId = s.Scope.Users.DepartmentId, SubmittedBy = student.Id, SubmittedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1),
                    SnapshotJson = JsonSerializer.Serialize(new RegistrationEvidence(
                        new TeamAcademicScopeDto("SINGLE_MAJOR", major, s.Scope.Users.DepartmentId, [new MajorRequirementDto(major, 1, 5, "Engineering")], Guid.NewGuid()),
                        new(1, 5, 1, "test"), s.Scope.OrganizationId, EvaluationDraftDatabaseFixture.Now.AddDays(-2), EvaluationDraftDatabaseFixture.Now.AddDays(2),
                        [new(student.Id, student.FullName, major, true)], [s.Scope.Users.DepartmentId],
                        MajorDepartmentIds: new Dictionary<long, long> { [major] = s.Scope.Users.DepartmentId })) });
            await db.SaveChangesAsync();
            var rubricId = (await db.ProjectPeriods.FindAsync(s.PeriodId))!.RubricId!.Value;
            SchemeComponentInput[] components = firstWeight.HasValue ?
                [new("First", "COMMON", null, rubricId, firstWeight.Value, firstWeight.Value, 1), new("Second", "COMMON", null, rubricId, 100-firstWeight.Value, 100-firstWeight.Value, 1)] :
                single ? [new("First", "COMMON", null, rubricId, 100, 100, 1)] :
                [new("First", "COMMON", null, rubricId, 60, 60, 1), new("Second", "COMMON", null, rubricId, 30, 30, 1), new("Supervisor", "COMMON", null, rubricId, 10, 10, 1)];
            var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
                new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Regression scoped scheme", threshold, components)));
            await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{draft.Id}/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
            published = await db.Set<EvaluationScheme>().Include(x => x.Components).SingleAsync(x => x.Id == draft.Id);
        }
        var name = type == "SUPERVISOR" ? "Supervisor" : user == s.Scope.Users.NewLecturer ? "Second" : "First";
        return new(user ?? s.Scope.Users.Lecturer, s.PeriodId, type, "COMMON", null, null, published.Components.Single(c => c.Name == name).Id);
    }
}
