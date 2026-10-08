using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    [Fact]
    public async Task Depart_D03_replacement_preview_excludes_current_supervisor_and_mutation_rechecks_availability()
    {
        var s = await database.SeedAsync();
        var project = await SeedProject(s);
        await using (var snapshotDb = database.CreateContext())
            await AcademicSnapshotFixture.AddAsync(snapshotDb, project.Id, project.PeriodId, s.DepartmentId, project.LeaderId, Now);
        await SeedActivationTemplate(s.Admin, project.SemesterId);
        var replacementProfileId = await AddProfile(s.NewLecturer);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(project.LeaderId);
        using var supervisor = app.CreateAuthenticatedClient(s.Lecturer);
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: ["DEPARTMENT_STAFF"]);
        using var outside = app.CreateAuthenticatedClient(s.OutsideStaff, roles: ["DEPARTMENT_STAFF"]);
        var request = await Body<SupervisorRequestDto>(await Send(leader, project.Id, s.ProfileId));
        await Body<SupervisorRequestDto>(await Respond(supervisor, request.Id, "accept"));
        long assignmentId;
        await using (var db = database.CreateContext())
            assignmentId = await db.SupervisorAssignments.Where(a => a.ProjectId == project.Id).Select(a => a.Id).SingleAsync();
        var url = $"/api/v1/supervisor-assignments/{assignmentId}";
        var detail = await Body<SupervisorAssignmentDto>(await staff.GetAsync(url));
        Assert.True(Assert.Single(detail.AllowedActions!, a => a.Code == "REPLACE").Allowed);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(url + "/replacement-candidates")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.GetAsync(url + "/replacement-candidates?pageSize=101")).StatusCode);
        var candidates = await Body<PagedResult<SupervisorReplacementCandidateDto>>(await staff.GetAsync(url + "/replacement-candidates"));
        Assert.DoesNotContain(candidates.Items, x => x.Candidate.Id == s.ProfileId);
        Assert.Contains(candidates.Items, x => x.Candidate.Id == replacementProfileId && x.Eligible);
        Assert.All(candidates.Items, x => Assert.Equal("NOT_REQUIRED", x.ExpertiseMatch));
        await using (var setup = database.CreateContext())
        {
            (await setup.SupervisorProfiles.FindAsync(replacementProfileId))!.IsAvailable = false;
            await setup.SaveChangesAsync();
        }
        var replacement = new { supervisorProfileId = replacementProfileId, reason = "DEPART acceptance replacement" };
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/replace", replacement)).StatusCode);
        await using (var setup = database.CreateContext())
        {
            Assert.Null((await setup.SupervisorAssignments.FindAsync(assignmentId))!.EndedAt);
            (await setup.SupervisorProfiles.FindAsync(replacementProfileId))!.IsAvailable = true;
            await setup.SaveChangesAsync();
        }
        var replaced = await Body<SupervisorAssignmentDto>(await staff.PostAsJsonAsync(url + "/replace", replacement));
        Assert.Equal(assignmentId, replaced.ReplacesAssignmentId);
        var ended = await Body<SupervisorAssignmentDto>(await staff.GetAsync(url));
        Assert.All(ended.AllowedActions!, a => Assert.False(a.Allowed));
        Assert.Contains("ASSIGNMENT_ENDED", ended.Reasons!);
    }
}
