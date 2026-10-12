using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.PeerEvaluations;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    private static string Peer(long id) => $"/api/v1/projects/{id}/peer-evaluations";
    private static void EnablePeer(SupervisorFactory app) => app.Services.GetRequiredService<IOptions<PeerEvaluationSettings>>().Value.Enabled = true;
    private static CreatePeerRoundRequest PeerInput() => new("Sprint feedback", new DateTimeOffset(Now.AddHours(-1)), new DateTimeOffset(Now.AddDays(1)));
    private async Task<RequestProject> PeerProject(SupervisorScenario s)
    {
        var m = await MentorProject(s);
        await using var db = database.CreateContext();
        (await db.Users.FindAsync(m.Project.LeaderId))!.MajorId = m.FirstMajor;
        (await db.Users.FindAsync(m.Project.MemberId))!.MajorId = m.SecondMajor;
        var department = (await db.Users.FindAsync(s.OutsideStaff))!.DepartmentId!.Value;
        (await db.Majors.FindAsync(m.SecondMajor))!.DepartmentId = department;
        foreach (var member in await db.TeamMembers.Where(r => r.TeamId == m.Project.TeamId).ToArrayAsync())
            member.JoinedAt = Now.AddDays(-1);
        await db.SaveChangesAsync();
        await AcademicSnapshotFixture.AddAsync(db, m.Project.Id, m.Project.PeriodId, s.DepartmentId, m.Project.LeaderId, Now);
        return m.Project;
    }
    private static async Task<PeerEvaluationDto> PeerSubmit(HttpClient client, long project, long round, long target)
    {
        var url = Peer(project) + $"/rounds/{round}/mine";
        var draft = await Body<PeerEvaluationDto>(await client.PutAsJsonAsync(url, new SavePeerEvaluationRequest(null, [new(target, 4, "Synthetic peer feedback")])));
        return await Body<PeerEvaluationDto>(await client.PostAsJsonAsync(url + "/submit", new PeerTokenRequest(draft.ConcurrencyToken)));
    }
    [Fact]
    public async Task Peer_lifecycle_is_private_immutable_department_redacted_and_never_changes_scores()
    {
        var s = await database.SeedAsync(); var p = await PeerProject(s);
        var evidence = new ApiAcceptanceEvidence("expansion-peer-lifecycle");
        using var app = new SupervisorFactory(database, clock: new Clock()) { AcceptanceEvidence = evidence }; EnablePeer(app);
        using var primary = app.CreateAuthenticatedClient(s.Lecturer);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        using var staff = app.CreateAuthenticatedClient(s.Staff);
        using var participating = app.CreateAuthenticatedClient(s.OutsideStaff);
        var url = Peer(p.Id);
        Assert.Equal("NO_OPEN_ROUND", (await Body<MyPeerEvaluationDto>(await leader.GetAsync(url + "/mine"))).Status);
        var round = await Body<PeerRoundDto>(await primary.PostAsJsonAsync(url + "/rounds", PeerInput()));
        Assert.Equal(2, round.MemberCount);
        var submitted = await PeerSubmit(leader, p.Id, round.Id, p.MemberId);
        Assert.False(submitted.AffectsOfficialResult);
        Assert.Null((await Body<MyPeerEvaluationDto>(await member.GetAsync(url + $"/mine?roundId={round.Id}"))).Submission);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(url + $"?roundId={round.Id}")).StatusCode);
        var studentRounds = await Body<PagedResult<PeerRoundDto>>(await leader.GetAsync(url + "/rounds"));
        Assert.False(Assert.Single(studentRounds.Items).CanSubmit);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(url + $"/rounds/{round.Id}/mine",
            new SavePeerEvaluationRequest(submitted.ConcurrencyToken, [new(p.MemberId, 1, "Change submitted")]))).StatusCode);
        await Body<PeerEvaluationDto>(await leader.PostAsJsonAsync(url + $"/rounds/{round.Id}/mine/submit", new PeerTokenRequest(submitted.ConcurrencyToken)));
        await PeerSubmit(member, p.Id, round.Id, p.LeaderId);
        Assert.Equal(2, (await Body<PagedResult<PeerEvaluationDto>>(await primary.GetAsync(url + $"?roundId={round.Id}"))).TotalCount);
        Assert.Equal(2, (await Body<PagedResult<PeerEvaluationDto>>(await staff.GetAsync(url + $"?roundId={round.Id}"))).TotalCount);
        var limited = await Body<PagedResult<PeerEvaluationDto>>(await participating.GetAsync(url + $"?roundId={round.Id}&pageSize=1"));
        Assert.Equal(1, limited.TotalCount); Assert.Equal(p.MemberId, Assert.Single(Assert.Single(limited.Items).Items).TargetUserId);
        var limitedRound = Assert.Single((await Body<PagedResult<PeerRoundDto>>(await participating.GetAsync(url + "/rounds"))).Items);
        Assert.Equal(p.MemberId, Assert.Single(limitedRound.Roster).UserId);
        foreach (var id in new[] { s.Admin, s.NewLecturer, s.OtherLecturer })
        {
            using var denied = app.CreateAuthenticatedClient(id);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(url + $"?roundId={round.Id}")).StatusCode);
        }
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.Set<PeerSubmission>().CountAsync(x => x.RoundId == round.Id && x.Status == "SUBMITTED"));
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.Action == "PEER_SUBMITTED" && a.EntityId == submitted.Id.ToString()));
        Assert.False(await check.Evaluations.AnyAsync(e => e.ProjectId == p.Id));
        Assert.False(await check.Set<ProjectResult>().AnyAsync(r => r.ProjectId == p.Id));
        Assert.False(await check.Set<WeeklyAssessment>().AnyAsync(r => r.ProjectId == p.Id));
        await evidence.SaveAsync(new { submitted = 2, scopedReviewerRows = 1, submissionAuditExactlyOnce = true, officialScoreWrites = 0 });
    }
    [Fact]
    public async Task Peer_rejects_self_foreign_duplicate_partial_stale_roster_and_revoked_member()
    {
        var s = await database.SeedAsync(); var p = await PeerProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock()); EnablePeer(app);
        using var primary = app.CreateAuthenticatedClient(s.Lecturer); using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var url = Peer(p.Id); var round = await Body<PeerRoundDto>(await primary.PostAsJsonAsync(url + "/rounds", PeerInput()));
        var mine = url + $"/rounds/{round.Id}/mine";
        foreach (var target in new[] { p.LeaderId, s.Student })
            Assert.Equal(HttpStatusCode.Forbidden, (await leader.PutAsJsonAsync(mine, new SavePeerEvaluationRequest(null, [new(target, 4, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PutAsJsonAsync(mine, new SavePeerEvaluationRequest(null,
            [new(p.MemberId, 4, null), new(p.MemberId, 4, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PutAsJsonAsync(mine, new SavePeerEvaluationRequest(null, [new(p.MemberId, 6, null)]))).StatusCode);
        var partial = await Body<PeerEvaluationDto>(await leader.PutAsJsonAsync(mine, new SavePeerEvaluationRequest(null, [])));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(mine + "/submit", new PeerTokenRequest(partial.ConcurrencyToken))).StatusCode);
        var complete = await Body<PeerEvaluationDto>(await leader.PutAsJsonAsync(mine,
            new SavePeerEvaluationRequest(partial.ConcurrencyToken, [new(p.MemberId, 4, null)])));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(mine + "/submit", new PeerTokenRequest(partial.ConcurrencyToken))).StatusCode);
        await using (var db = database.CreateContext())
        {
            var member = await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId);
            member.MembershipGeneration = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(mine + "/submit", new PeerTokenRequest(complete.ConcurrencyToken))).StatusCode);
        await Body<PeerRoundDto>(await primary.PostAsJsonAsync(url + $"/rounds/{round.Id}/close", new PeerTokenRequest(round.ConcurrencyToken)));
        var next = await Body<PeerRoundDto>(await primary.PostAsJsonAsync(url + "/rounds", PeerInput()));
        Assert.NotEqual(round.Id, next.Id);
        await using (var db = database.CreateContext())
        { (await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.LeaderId)).LeftAt = Now; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PutAsJsonAsync(url + $"/rounds/{next.Id}/mine",
            new SavePeerEvaluationRequest(null, [new(p.MemberId, 4, null)]))).StatusCode);
    }
    [Fact]
    public async Task Peer_concurrent_rounds_single_winner_and_audit_failure_rolls_back()
    {
        var s = await database.SeedAsync(); var p = await PeerProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock()); EnablePeer(app);
        using var primary = app.CreateAuthenticatedClient(s.Lecturer);
        var url = Peer(p.Id);
        var attempts = await Task.WhenAll(primary.PostAsJsonAsync(url + "/rounds", PeerInput()), primary.PostAsJsonAsync(url + "/rounds", PeerInput()));
        Assert.Single(attempts.Where(r => r.StatusCode == HttpStatusCode.OK)); Assert.Single(attempts.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var round = await Body<PeerRoundDto>(attempts.Single(r => r.IsSuccessStatusCode));
        using var failure = new SupervisorFactory(database, failAudit: true, clock: new Clock()); EnablePeer(failure);
        using var student = failure.CreateAuthenticatedClient(p.LeaderId);
        Assert.Equal(HttpStatusCode.InternalServerError, (await student.PutAsJsonAsync(url + $"/rounds/{round.Id}/mine",
            new SavePeerEvaluationRequest(null, [new(p.MemberId, 4, null)]))).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Set<PeerSubmission>().AnyAsync(x => x.RoundId == round.Id));
        Assert.Equal(1, await db.Set<PeerRound>().CountAsync(x => x.ProjectId == p.Id));
    }
    [Fact]
    public async Task Peer_disabled_window_and_archived_write_denial()
    {
        var s = await database.SeedAsync(); var p = await PeerProject(s);
        var clock = new AdvancingClock();
        using var app = new SupervisorFactory(database, clock: clock);
        using var primary = app.CreateAuthenticatedClient(s.Lecturer); using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var url = Peer(p.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(url + "/mine")).StatusCode); EnablePeer(app);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url + "/rounds")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsJsonAsync(url + "/rounds", PeerInput())).StatusCode);
        var round = await Body<PeerRoundDto>(await primary.PostAsJsonAsync(url + "/rounds", PeerInput()));
        clock.Expire();
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(url + $"/rounds/{round.Id}/mine",
            new SavePeerEvaluationRequest(null, [new(p.MemberId, 4, null)]))).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(p.Id))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(url + $"/rounds/{round.Id}/close", new PeerTokenRequest(round.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync(url + $"/mine?roundId={round.Id}")).StatusCode);
    }
}
