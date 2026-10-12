using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Application.Features.WorkingAgreements;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class WorkingAgreementEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static string Url(long project) => $"/api/v1/projects/{project}/working-agreement";
    private static SaveWorkingAgreementRequest Input(int version = 0, string? token = null) => new(version, token, [new("Communication", "Meet each Monday")]);
    private static AgreementTokenRequest Token(WorkingAgreementDto row) => new(row.Id, row.ConcurrencyToken);
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private async Task<EvaluationScenario> Seed(bool required = false)
    {
        var s = await database.Seed(false);
        await using var db = database.CreateContext();
        (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(p => p.ProjectId == s.ProjectId);
        var policy = new PeriodPolicyVersion { ProjectPeriodId = s.PeriodId, Version = 1, Status = "LOCKED",
            EffectiveFrom = EvaluationDraftDatabaseFixture.Now.AddDays(-2), EffectiveTo = EvaluationDraftDatabaseFixture.Now.AddDays(2),
            SnapshotJson = JsonSerializer.Serialize(new PeriodPolicyFields("SINGLE_MAJOR", "STUDENT_PROPOSAL", 1, 5, 1, 5, required)) };
        db.Add(policy); await db.SaveChangesAsync();
        db.Add(new PeriodPolicyUsage { PolicyVersionId = policy.Id, EntityType = "PROJECT_SUBMISSION", EntityId = snapshot.Id,
            CreatedAt = EvaluationDraftDatabaseFixture.Now.AddHours(-1) });
        await db.SaveChangesAsync(); return s;
    }
    [Fact]
    public async Task Publish_accept_successor_preserves_version_and_audit_and_rerun()
    {
        var s = await Seed(true); using var app = new AgreementFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.NoContent, (await leader.GetAsync(Url(s.ProjectId))).StatusCode);
        var draft = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input()));
        Assert.True(draft.Required); Assert.Equal("DRAFT", draft.Status);
        var active = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(draft)));
        Assert.Single(active.Readiness.Reasons); Assert.Equal(1, active.MemberCount);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(draft))).StatusCode);
        var accepted = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active)));
        Assert.True(accepted.AcceptedByMe); Assert.Equal("READY", accepted.Readiness.Status);
        await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active)));
        var next = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input(1)));
        var nextActive = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(next)));
        Assert.Empty(nextActive.Acceptances); Assert.Equal(2, nextActive.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active))).StatusCode);
        var history = await Body<PagedResult<WorkingAgreementDto>>(await leader.GetAsync(Url(s.ProjectId) + "/history?pageSize=2"));
        Assert.Equal(2, history.TotalCount); Assert.Equal("SUPERSEDED", history.Items.Last().Status);
        Assert.True(history.Items.Last().AcceptedByMe);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityId == active.Id.ToString() && a.Action == "WORKING_AGREEMENT_ACCEPTED"));
        var sql = await File.ReadAllTextAsync(Path.Combine(Root(), "db/changes/20261012_add_working_agreements.sql"));
        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"(?im)^\s*GO\s*$"))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
        Assert.Equal(2, await db.Set<WorkingAgreement>().CountAsync(a => a.ProjectId == s.ProjectId));
    }
    [Fact]
    public async Task Scope_disabled_archived_and_roster_change_fail_closed()
    {
        var s = await Seed(); using var app = new AgreementFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(s.ProjectId))).StatusCode);
        foreach (var id in new[] { s.Scope.Users.Admin, s.Scope.Users.OtherLecturer, s.Scope.Users.Lecturer })
        {
            using var denied = app.CreateAuthenticatedClient(id);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(Url(s.ProjectId))).StatusCode);
        }
        using var disabledApp = new AgreementFactory(database, enabled: false);
        using var disabled = disabledApp.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.NotFound, (await disabled.GetAsync(Url(s.ProjectId))).StatusCode);
        var draft = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input()));
        var active = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(draft)));
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Url(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active))).StatusCode);
        await using (var db = database.CreateContext())
        {
            var project = (await db.Projects.FindAsync(s.ProjectId))!;
            db.TeamMembers.Add(new() { TeamId = project.TeamId, AcademicSemesterId = s.Scope.SemesterId, UserId = s.Scope.Users.NewLecturer });
            await db.SaveChangesAsync();
        }
        var stale = await Body<WorkingAgreementDto>(await leader.GetAsync(Url(s.ProjectId)));
        Assert.Contains("WORKING_AGREEMENT_ROSTER_CHANGED", stale.Readiness.Reasons);
        Assert.False(stale.Actions.Single(a => a.Code == "accept_working_agreement").Allowed);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active))).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await leader.GetAsync(Url(s.ProjectId))).StatusCode);
    }
    [Fact]
    public async Task Concurrent_create_and_audit_failure_do_not_leave_partial_rows()
    {
        var s = await Seed(); using var app = new AgreementFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input())));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var draft = await Body<WorkingAgreementDto>(responses.Single(r => r.IsSuccessStatusCode));
        using var failingApp = new AgreementFactory(database, failAudit: true);
        using var failing = failingApp.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(draft))).StatusCode);
        var after = await Body<WorkingAgreementDto>(await leader.GetAsync(Url(s.ProjectId)));
        Assert.Equal("DRAFT", after.Status); Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken); Assert.Equal(0, after.MemberCount);
        var active = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(draft)));
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active))).StatusCode);
        Assert.Empty((await Body<WorkingAgreementDto>(await leader.GetAsync(Url(s.ProjectId)))).Acceptances);
    }
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "db/changes"))) dir = dir.Parent;
        return dir!.FullName;
    }
    [Fact]
    public async Task Rejoining_same_user_requires_new_membership_consent_and_foreign_version_is_not_readable()
    {
        var s = await Seed(); var foreign = await Seed(); using var app = new AgreementFactory(database);
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var other = app.CreateAuthenticatedClient(foreign.Scope.Users.Student);
        var draft = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input()));
        var active = await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/publish", Token(draft)));
        await Body<WorkingAgreementDto>(await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active)));
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(Url(s.ProjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(Url(foreign.ProjectId) + "/acceptances", Token(active))).StatusCode);
        await using (var db = database.CreateContext())
        {
            var member = await db.TeamMembers.SingleAsync(m => m.UserId == s.Scope.Users.Student && m.LeftAt == null);
            member.LeftAt = EvaluationDraftDatabaseFixture.Now;
            await db.SaveChangesAsync();
            db.TeamMembers.Add(new() { TeamId = member.TeamId, AcademicSemesterId = member.AcademicSemesterId,
                UserId = member.UserId, IsLeader = true, JoinedAt = EvaluationDraftDatabaseFixture.Now });
            await db.SaveChangesAsync();
        }
        var row = await Body<WorkingAgreementDto>(await leader.GetAsync(Url(s.ProjectId)));
        Assert.Contains("WORKING_AGREEMENT_ROSTER_CHANGED", row.Readiness.Reasons);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Url(s.ProjectId) + "/acceptances", Token(active))).StatusCode);
    }
    [Fact]
    public async Task Only_admin_changes_requirement_and_staff_cannot_publish_admin_toggle()
    {
        var s = await Seed(); using var app = new AgreementFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(s.Scope.Users.Admin);
        var url = $"/api/v1/project-periods/{s.PeriodId}";
        var before = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"));
        var request = new UpdatePeriodPolicyRequest(before.Version, "SUCCESSOR", before.Policy with { RequireWorkingAgreement = true },
            EvaluationDraftDatabaseFixture.Now, EvaluationDraftDatabaseFixture.Now.AddHours(1));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(url + "/policy", request)).StatusCode);
        var draft = await Body<PeriodPolicyDto>(await admin.PutAsJsonAsync(url + "/policy", request));
        var publish = request with { Operation = "PUBLISH", ExpectedVersion = draft.Version, ConcurrencyToken = draft.ConcurrencyToken };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(url + "/policy", publish)).StatusCode);
        await Body<PeriodPolicyDto>(await admin.PutAsJsonAsync(url + "/policy", publish));
        using var leader = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var agreement = await Body<WorkingAgreementDto>(await leader.PutAsJsonAsync(Url(s.ProjectId) + "/draft", Input()));
        Assert.False(agreement.Required);
        await using var db = database.CreateContext();
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(s.ProjectId))!.Status);
    }
    private sealed class AgreementFactory(EvaluationDraftDatabaseFixture database, bool enabled = true, bool failAudit = false) : EvaluationFactory(database, failAudit)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["WorkingAgreement:Enabled"] = enabled.ToString() }));
        }
    }
}
