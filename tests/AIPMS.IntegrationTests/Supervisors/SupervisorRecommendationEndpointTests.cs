using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    private static string Recommendations(long project) => $"/api/v1/projects/{project}/supervisor-recommendations";
    private static void EnableRecommendations(SupervisorFactory app) =>
        app.Services.GetRequiredService<IOptions<SupervisorRecommendationSettings>>().Value.Enabled = true;
    private async Task FreezeRecommendations(SupervisorScenario s, RequestProject p)
    {
        await using var db = database.CreateContext();
        await AcademicSnapshotFixture.AddAsync(db, p.Id, p.PeriodId, s.DepartmentId, p.LeaderId, Now);
    }

    [Fact]
    public async Task Recommendations_ignore_expired_pending_requests_before_count_without_read_side_mutation()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s); await FreezeRecommendations(s, p);
        using var app = new SupervisorFactory(database, clock: new Clock()); EnableRecommendations(app);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var url = Recommendations(p.Id) + "?pageSize=1";
        Assert.Empty((await Body<SupervisorRecommendationPreview>(await leader.GetAsync(url))).Candidates.Items);
        await using (var db = database.CreateContext())
        { var row = (await db.SupervisorRequests.FindAsync(request.Id))!; row.ExpiresAt = Now; await db.SaveChangesAsync(); }
        var page = await Body<SupervisorRecommendationPreview>(await leader.GetAsync(url));
        Assert.Equal(1, page.Candidates.TotalCount); Assert.Equal(s.ProfileId, Assert.Single(page.Candidates.Items).Candidate.Id);
        await using (var db = database.CreateContext()) Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task Recommendations_rank_all_eligible_candidates_before_paging_and_persist_readback_audit()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s); await FreezeRecommendations(s, p);
        var match = await AddProfile(s.NewLecturer);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.NewLecturer))!.FullName = "ZZ matched";
            db.SupervisorExpertises.Add(new() { SupervisorProfileId = match, ExpertiseName = " se " });
            await db.SaveChangesAsync();
        }
        var evidence = new ApiAcceptanceEvidence("expansion-supervisor-recommendations");
        using var app = new SupervisorFactory(database, clock: new Clock()) { AcceptanceEvidence = evidence }; EnableRecommendations(app);
        using var client = app.CreateAuthenticatedClient(p.LeaderId);
        var url = Recommendations(p.Id);
        var preview = await Body<SupervisorRecommendationPreview>(await client.GetAsync(url + "?pageSize=1"));
        Assert.Equal(2, preview.Candidates.TotalCount);
        var first = Assert.Single(preview.Candidates.Items);
        Assert.Equal(match, first.Candidate.Id); Assert.Equal(1m, first.Score); Assert.Equal("MATCHED", first.ExpertiseStatus);
        var second = await Body<SupervisorRecommendationPreview>(await client.GetAsync(url + "?page=2&pageSize=1"));
        Assert.Equal(s.ProfileId, Assert.Single(second.Candidates.Items).Candidate.Id);
        Assert.Equal("NO_MATCH", second.Candidates.Items[0].ExpertiseStatus);
        var run = await Body<SupervisorRecommendationRun>(await client.PostAsJsonAsync(url, new SupervisorRecommendationRequest(PageSize: 1)));
        var saved = await Body<SupervisorRecommendationRun>(await client.GetAsync(url + $"/history/{run.Id}"));
        Assert.Equal(match, Assert.Single(saved.Items).Candidate.Id); Assert.True(saved.Items[0].CurrentlyEligible);
        var history = await Body<PagedResult<SupervisorRecommendationRunSummary>>(await client.GetAsync(url + "/history?pageSize=1"));
        Assert.Equal(run.Id, Assert.Single(history.Items).Id);
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.Set<SupervisorRecommendationEntry>().CountAsync(x => x.RunId == run.Id));
        Assert.True(await check.AuditLogs.AnyAsync(a => a.Action == "SUPERVISOR_RECOMMENDATIONS_CREATED" && a.EntityId == run.Id.ToString()));
        Assert.False(await check.SupervisorRequests.AnyAsync(r => r.ProjectId == p.Id));
        Assert.False(await check.SupervisorAssignments.AnyAsync(r => r.ProjectId == p.Id));
        (await check.SupervisorProfiles.FindAsync(match))!.MaxActiveProjects = 0;
        await check.SaveChangesAsync();
        var stale = await Body<SupervisorRecommendationRun>(await client.GetAsync(url + $"/history/{run.Id}"));
        Assert.False(stale.Items[0].CurrentlyEligible); Assert.Equal(1m, stale.Items[0].Score);
        var reduced = await Body<SupervisorRecommendationPreview>(await client.GetAsync(url));
        Assert.Equal(1, reduced.Candidates.TotalCount); Assert.Equal(s.ProfileId, Assert.Single(reduced.Candidates.Items).Candidate.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(client, p.Id, match)).StatusCode);
        await evidence.SaveAsync(new { persistedRun = run.Id, items = 1, audit = true, noAssignment = true,
            staleCapacityDenied = true, rankingBeforePaging = true });
    }

    [Fact]
    public async Task Recommendations_deny_missing_scope_foreign_ids_disabled_accounts_invalid_input_and_rollback_audit()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s); var url = Recommendations(p.Id);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(url)).StatusCode);
        EnableRecommendations(app);
        using var anon = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.GetAsync(url)).StatusCode);
        await FreezeRecommendations(s, p);
        foreach (var user in new[] { s.Student, s.OtherLecturer, s.OutsideStaff })
        {
            using var other = app.CreateAuthenticatedClient(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(url)).StatusCode);
        }
        foreach (var query in new[] { "?pageSize=101", "?page=0", "?assignmentType=WRONG", "?assignmentType=DISCIPLINE_MENTOR", "?majorId=1" })
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(url + query)).StatusCode);
        var run = await Body<SupervisorRecommendationRun>(await leader.PostAsJsonAsync(url, new SupervisorRecommendationRequest()));
        var otherProject = await SeedProject(s); await FreezeRecommendations(s, otherProject);
        using var otherLeader = app.CreateAuthenticatedClient(otherProject.LeaderId);
        Assert.Equal(HttpStatusCode.NotFound, (await otherLeader.GetAsync(Recommendations(otherProject.Id) + $"/history/{run.Id}")).StatusCode);
        using var failApp = new SupervisorFactory(database, failAudit: true, clock: new Clock()); EnableRecommendations(failApp);
        using var fail = failApp.CreateAuthenticatedClient(p.LeaderId);
        Assert.Equal(HttpStatusCode.InternalServerError, (await fail.PostAsJsonAsync(url, new SupervisorRecommendationRequest())).StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.Set<SupervisorRecommendation>().CountAsync(r => r.ProjectId == p.Id));
        (await check.Users.FindAsync(p.LeaderId))!.Status = "INACTIVE"; await check.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(url + $"/history/{run.Id}")).StatusCode);
    }

    [Fact]
    public async Task Recommendations_mentor_normalizes_exact_major_and_history_survives_filled_slot()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock()); EnableRecommendations(app);
        using var leader = app.CreateAuthenticatedClient(m.Project.LeaderId);
        var url = Recommendations(m.Project.Id);
        var request = new SupervisorRecommendationRequest("DISCIPLINE_MENTOR", m.FirstMajor);
        var run = await Body<SupervisorRecommendationRun>(await leader.PostAsJsonAsync(url, request));
        Assert.Equal(m.Mentor, Assert.Single(run.Items).Candidate.Id);
        Assert.Equal("DISCIPLINE_MENTOR", run.Items[0].Candidate.AssignmentType);
        var sent = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        using var mentor = app.CreateAuthenticatedClient(s.NewLecturer);
        await Body<SupervisorRequestDto>(await Respond(mentor, sent.Id, "accept"));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(url, request)).StatusCode);
        var history = await Body<SupervisorRecommendationRun>(await leader.GetAsync(url + $"/history/{run.Id}"));
        Assert.False(history.Items[0].CurrentlyEligible); Assert.Contains("SELECTION_NOT_AVAILABLE", history.Items[0].CurrentReasons);
    }

    [Fact]
    public async Task Recommendations_participating_staff_and_mentor_history_are_scoped_before_count()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        await using (var db = database.CreateContext())
        {
            var department = (await db.Users.FindAsync(s.OutsideStaff))!.DepartmentId!.Value;
            (await db.Majors.FindAsync(m.SecondMajor))!.DepartmentId = department;
            await db.SaveChangesAsync();
            await AcademicSnapshotFixture.AddAsync(db, m.Project.Id, m.Project.PeriodId, s.DepartmentId, m.Project.LeaderId, Now);
        }
        using var app = new SupervisorFactory(database, clock: new Clock()); EnableRecommendations(app);
        using var leader = app.CreateAuthenticatedClient(m.Project.LeaderId);
        using var staff = app.CreateAuthenticatedClient(s.OutsideStaff);
        var url = Recommendations(m.Project.Id);
        var first = await Body<SupervisorRecommendationRun>(await leader.PostAsJsonAsync(url, new SupervisorRecommendationRequest("DISCIPLINE_MENTOR", m.FirstMajor)));
        var second = await Body<SupervisorRecommendationRun>(await leader.PostAsJsonAsync(url, new SupervisorRecommendationRequest("DISCIPLINE_MENTOR", m.SecondMajor)));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(url + $"/history/{first.Id}")).StatusCode);
        await Body<SupervisorRecommendationRun>(await staff.GetAsync(url + $"/history/{second.Id}"));
        var staffHistory = await Body<PagedResult<SupervisorRecommendationRunSummary>>(await staff.GetAsync(url + "/history?pageSize=1"));
        Assert.Equal(1, staffHistory.TotalCount); Assert.Equal(second.Id, Assert.Single(staffHistory.Items).Id);
        var request = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        using var mentor = app.CreateAuthenticatedClient(s.NewLecturer);
        await Body<SupervisorRequestDto>(await Respond(mentor, request.Id, "accept"));
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(url + $"/history/{second.Id}")).StatusCode);
        Assert.Equal(first.Id, Assert.Single((await Body<PagedResult<SupervisorRecommendationRunSummary>>(await mentor.GetAsync(url + "/history"))).Items).Id);
        await using var check = database.CreateContext();
        var assignment = await check.SupervisorAssignments.SingleAsync(a => a.ProjectId == m.Project.Id && a.AssignmentType == "DISCIPLINE_MENTOR");
        assignment.EndedAt = Now; await check.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(url + $"/history/{first.Id}")).StatusCode);
    }

    [Fact]
    public async Task Recommendations_exclude_D17_before_count_and_distinguish_unknown_expertise_with_stable_ties()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s); await FreezeRecommendations(s, p);
        var other = await AddProfile(s.NewLecturer);
        await using (var db = database.CreateContext())
        {
            db.SupervisorExpertises.RemoveRange(await db.SupervisorExpertises.Where(e => e.SupervisorProfileId == s.ProfileId).ToArrayAsync());
            (await db.SupervisorProfiles.FindAsync(other))!.MaxActiveProjects = 3;
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, clock: new Clock()); EnableRecommendations(app);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var url = Recommendations(p.Id);
        var page = await Body<SupervisorRecommendationPreview>(await leader.GetAsync(url));
        Assert.Equal(new[] { s.ProfileId, other }.Order(), page.Candidates.Items.Select(i => i.Candidate.Id));
        Assert.All(page.Candidates.Items, i => { Assert.Equal(0m, i.Score); Assert.Equal("UNKNOWN", i.ExpertiseStatus); });
        await using (var db = database.CreateContext())
        {
            var rubric = new Rubric { Code = Guid.NewGuid().ToString("N"), Name = "Recommendation conflict fixture",
                CreatedBy = s.Staff, DepartmentId = s.DepartmentId, AcademicSemesterId = p.SemesterId };
            db.Rubrics.Add(rubric); await db.SaveChangesAsync();
            db.Add(new EvaluationAssignment { ProjectId = p.Id, EvaluatorId = s.Lecturer, RubricId = rubric.Id,
                ProjectPeriodId = p.PeriodId, DepartmentId = s.DepartmentId, AssignedBy = s.Staff, AssignedAt = Now,
                ConcurrencyToken = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var filtered = await Body<SupervisorRecommendationPreview>(await leader.GetAsync(url + "?pageSize=1"));
        Assert.Equal(1, filtered.Candidates.TotalCount); Assert.Equal(other, Assert.Single(filtered.Candidates.Items).Candidate.Id);
    }

    [Fact]
    public async Task Recommendations_without_frozen_academic_scope_returns_409_with_AcademicScopeUnknown()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        EnableRecommendations(app);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var url = Recommendations(p.Id);

        var getResponse = await leader.GetAsync(url);
        Assert.Equal(HttpStatusCode.Conflict, getResponse.StatusCode);
        var getJson = await getResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.NotNull(getJson);
        Assert.Equal("ACADEMIC_SCOPE_UNKNOWN", getJson.RootElement.GetProperty("code").GetString());

        var postResponse = await leader.PostAsJsonAsync(url, new SupervisorRecommendationRequest());
        Assert.Equal(HttpStatusCode.Conflict, postResponse.StatusCode);
        var postJson = await postResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        Assert.NotNull(postJson);
        Assert.Equal("ACADEMIC_SCOPE_UNKNOWN", postJson.RootElement.GetProperty("code").GetString());
    }
}
