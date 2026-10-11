using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    [Fact]
    public async Task V5_capabilities_match_lead_mutations_and_admin_is_not_an_academic_publisher()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin, roles: ["ADMIN"]);
        using var anonymous = app.CreateClient();
        var url = $"/api/v1/projects/{s.ProjectId}/my-capabilities";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        var mine = await Body<ProjectCapabilitiesDto>(await staff.GetAsync(url));
        Assert.Equal("FROZEN_REGISTRATION_SNAPSHOT", mine.AcademicScopeProvenance);
        Assert.Equal(s.Scope.Users.DepartmentId, mine.LeadDepartmentId);
        Assert.True(mine.Actions.Single(a => a.Code == "manage_evaluation_scheme").Allowed);
        Assert.False(mine.Actions.Single(a => a.Code == "publish_project_result").Allowed);
        var platform = await Body<ProjectCapabilitiesDto>(await admin.GetAsync(url));
        Assert.False(platform.Actions.Single(a => a.Code == "manage_evaluation_scheme").Allowed);
        Assert.False(platform.Actions.Single(a => a.Code == "manage_evaluators").Allowed);
        Assert.False(platform.Actions.Single(a => a.Code == "publish_project_result").Allowed);
        var input = new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Lead authority", 5,
            [new("Common", "COMMON", null, s.RubricId, 100, 100, 1)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/evaluation-schemes", input)).StatusCode);
        var scheme = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", input));
        mine = await Body<ProjectCapabilitiesDto>(await staff.GetAsync(url));
        Assert.Equal(scheme.Id, mine.SchemeId);
        Assert.Equal(scheme.ConcurrencyToken, mine.SchemeConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/result",
            new { confirmationToken = new string('a', 64) })).StatusCode);
        using var swagger = System.Text.Json.JsonDocument.Parse(await anonymous.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(swagger.RootElement.GetProperty("paths").TryGetProperty("/api/v1/projects/{projectId}/my-capabilities", out _));
    }

    [Fact]
    public async Task D17_mentor_can_evaluate_but_has_no_project_structure_authority()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        await using var db = database.CreateContext();
        var row = await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId);
        var major = await db.ProjectMajors.Where(p => p.ProjectId == s.ProjectId).Select(p => p.MajorId).SingleAsync();
        row.IsPrimary = false; row.AssignmentType = "DISCIPLINE_MENTOR"; row.MajorId = major;
        row.SupervisorProfileId = s.Scope.Users.ProfileId;
        await db.SaveChangesAsync();
        var assigned = await Assign(staff, s);
        Assert.Equal(s.Scope.Users.Lecturer, assigned.EvaluatorId);
        Assert.False(await AIPMS.Infrastructure.Services.Projects.ProjectExecutionAuthority.IsManagerAsync(
            db, s.ProjectId, s.Scope.Users.Lecturer, default));
    }
}
