using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.WeeklyAssessments;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class WeeklyAssessmentEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string Base(long project) => $"/api/v1/projects/{project}/weekly-assessments";
    private async Task<(EvaluationScenario S, long Primary)> Seed()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext();
        var primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId && a.IsPrimary).Select(a => a.SupervisorProfile.UserId).SingleAsync();
        return (s, primary);
    }
    private static SaveWeeklyAssessmentRequest Input(EvaluationScenario s, int week, decimal? score = 8) => new(s.Scope.Users.Student,
        week, s.RubricId, 0, [new(s.Criteria[0], score, null), new(s.Criteria[1], score * 2, null)]);

    [Fact]
    public async Task Primary_saves_submits_and_revises_frozen_weekly_rubric_without_official_result_changes()
    {
        using var app = new WeeklyFactory(database); var (s, primaryId) = await Seed();
        using var primary = app.CreateAuthenticatedClient(primaryId); using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var calendar = await Body<WeeklyAssessmentCalendar>(await primary.GetAsync(Base(s.ProjectId) + "/weeks"));
        var week = calendar.Weeks.Last(w => w.HasStarted);
        Assert.Equal("Asia/Ho_Chi_Minh", calendar.TimeZoneId);
        var input = Input(s, week.WeekNumber);
        var draft = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input));
        Assert.Equal(8m, draft.TotalScore); Assert.True(draft.CanSubmit); Assert.True(draft.IsDemo); Assert.False(draft.AffectsOfficialResult);
        Assert.Equal(week.StartDate, draft.StartDate);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync($"/api/v1/weekly-assessments/{draft.Id}")).StatusCode);
        Assert.Empty((await Body<PagedResult<WeeklyAssessmentDto>>(await student.GetAsync(Base(s.ProjectId)))).Items);
        var changed = input with { ExpectedLatestVersion = 1, ConcurrencyToken = draft.ConcurrencyToken, Scores = [new(s.Criteria[0], 0, null), new(s.Criteria[1], 0, null)] };
        var updated = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", changed));
        Assert.Equal(0m, updated.TotalScore);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", changed)).StatusCode);
        var submitted = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync($"/api/v1/weekly-assessments/{draft.Id}/submit", new SubmitWeeklyAssessmentRequest(updated.ConcurrencyToken)));
        Assert.Equal("SUBMITTED", submitted.Status); Assert.False(submitted.CanEdit);
        Assert.Equal(0m, (await Body<WeeklyAssessmentDto>(await student.GetAsync($"/api/v1/weekly-assessments/{draft.Id}"))).TotalScore);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with { ExpectedLatestVersion = 1 })).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Rubrics.FindAsync(s.RubricId))!.Name = "Changed live rubric";
            await db.SaveChangesAsync();
        }
        var correction = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft",
            input with { ExpectedLatestVersion = 1, RevisionReason = "Correct data entry" }));
        Assert.Equal(2, correction.Version); Assert.Equal(draft.RubricName, correction.RubricName);
        await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync($"/api/v1/weekly-assessments/{correction.Id}/submit", new SubmitWeeklyAssessmentRequest(correction.ConcurrencyToken)));
        var history = await Body<PagedResult<WeeklyAssessmentDto>>(await student.GetAsync(Base(s.ProjectId) + "?pageSize=1"));
        Assert.Equal(2, history.TotalCount); Assert.Equal(2, history.Items.Single().Version);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Evaluations.Where(e => e.ProjectId == s.ProjectId).ToArrayAsync());
        Assert.Empty(await verify.Set<StudentResult>().Where(e => e.ProjectId == s.ProjectId).ToArrayAsync());
        Assert.Equal(2, await verify.AuditLogs.CountAsync(a => a.Action == "WEEKLY_ASSESSMENT_SUBMITTED" && (a.EntityId == draft.Id.ToString() || a.EntityId == correction.Id.ToString())));
    }

    [Fact]
    public async Task Missing_scores_future_week_foreign_student_and_nonprimary_fail_closed()
    {
        using var app = new WeeklyFactory(database); var (s, primaryId) = await Seed();
        using var primary = app.CreateAuthenticatedClient(primaryId);
        var calendar = await Body<WeeklyAssessmentCalendar>(await primary.GetAsync(Base(s.ProjectId) + "/weeks"));
        var input = Input(s, calendar.Weeks.Last(w => w.HasStarted).WeekNumber, null);
        foreach (var actor in new[] { s.Scope.Users.Admin, s.Scope.Users.Staff, s.Scope.Users.Lecturer, s.Scope.Users.OtherLecturer, s.Scope.Users.Student })
        {
            using var client = app.CreateAuthenticatedClient(actor);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with { WeekNumber = calendar.Weeks.First(w => !w.HasStarted).WeekNumber })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with { StudentId = s.Scope.Users.OtherLecturer })).StatusCode);
        var draft = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input));
        Assert.Null(draft.TotalScore); Assert.False(draft.CanSubmit);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync($"/api/v1/weekly-assessments/{draft.Id}/submit", new SubmitWeeklyAssessmentRequest(draft.ConcurrencyToken))).StatusCode);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(Base(s.ProjectId) + $"?studentId={s.Scope.Users.OtherLecturer}")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(s.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with { ExpectedLatestVersion = 1, ConcurrencyToken = draft.ConcurrencyToken })).StatusCode);
    }

    [Fact]
    public async Task Rubric_selection_and_concurrent_draft_creation_are_scoped_and_versioned()
    {
        using var app = new WeeklyFactory(database); var (s, primaryId) = await Seed();
        using var primary = app.CreateAuthenticatedClient(primaryId); using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var rubrics = await Body<PagedResult<RubricRecord>>(await primary.GetAsync(Base(s.ProjectId) + "/rubrics?pageSize=1"));
        Assert.Equal(s.RubricId, rubrics.Items.Single().Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(Base(s.ProjectId) + "/rubrics")).StatusCode);
        var week = (await Body<WeeklyAssessmentCalendar>(await primary.GetAsync(Base(s.ProjectId) + "/weeks"))).Weeks.Last(w => w.HasStarted).WeekNumber;
        var foreign = await database.Seed(false);
        var input = Input(s, week);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with { RubricId = foreign.RubricId })).StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input)));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var draft = await Body<WeeklyAssessmentDto>(responses.Single(r => r.IsSuccessStatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, (await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input with {
            ExpectedLatestVersion = 1, ConcurrencyToken = draft.ConcurrencyToken, Scores = [new(long.MaxValue, 8, null)] })).StatusCode);
        await using var db = database.CreateContext();
        Assert.Single(await db.Set<WeeklyAssessment>().Where(w => w.ProjectId == s.ProjectId).ToArrayAsync());
    }

    [Fact]
    public async Task Replacement_primary_creates_new_draft_and_audit_failure_rolls_back_submit()
    {
        using var app = new WeeklyFactory(database); var (s, primaryId) = await Seed();
        using var primary = app.CreateAuthenticatedClient(primaryId);
        var week = (await Body<WeeklyAssessmentCalendar>(await primary.GetAsync(Base(s.ProjectId) + "/weeks"))).Weeks.Last(w => w.HasStarted).WeekNumber;
        var input = Input(s, week);
        var draft = await Body<WeeklyAssessmentDto>(await primary.PostAsJsonAsync(Base(s.ProjectId) + "/draft", input));
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId)).EndedAt = EvaluationDraftDatabaseFixture.Now;
            var profile = new M.SupervisorProfile { UserId = s.Scope.Users.NewLecturer };
            var request = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfile = profile, RequestedBy = s.Scope.Users.Student,
                Status = "ACCEPTED", RequestedAt = EvaluationDraftDatabaseFixture.Now, RespondedAt = EvaluationDraftDatabaseFixture.Now };
            db.SupervisorAssignments.Add(new() { ProjectId = s.ProjectId, SupervisorRequest = request, SupervisorProfile = profile,
                AssignmentType = "PRIMARY", IsPrimary = true, AssignedAt = EvaluationDraftDatabaseFixture.Now });
            await db.SaveChangesAsync();
        }
        using var replacement = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync($"/api/v1/weekly-assessments/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await replacement.PostAsJsonAsync($"/api/v1/weekly-assessments/{draft.Id}/submit", new SubmitWeeklyAssessmentRequest(draft.ConcurrencyToken))).StatusCode);
        var successor = await Body<WeeklyAssessmentDto>(await replacement.PostAsJsonAsync(Base(s.ProjectId) + "/draft",
            input with { ExpectedLatestVersion = 1, RevisionReason = "Primary handover" }));
        Assert.Equal("SUPERSEDED", (await Body<WeeklyAssessmentDto>(await replacement.GetAsync($"/api/v1/weekly-assessments/{draft.Id}"))).Status);
        app.FailAudit = true;
        var submit = $"/api/v1/weekly-assessments/{successor.Id}/submit";
        Assert.Equal(HttpStatusCode.InternalServerError, (await replacement.PostAsJsonAsync(submit, new SubmitWeeklyAssessmentRequest(successor.ConcurrencyToken))).StatusCode);
        Assert.Equal("DRAFT", (await Body<WeeklyAssessmentDto>(await replacement.GetAsync($"/api/v1/weekly-assessments/{successor.Id}"))).Status);
        app.FailAudit = false;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => replacement.PostAsJsonAsync(submit, new SubmitWeeklyAssessmentRequest(successor.ConcurrencyToken))));
        Assert.Single(results.Where(r => r.IsSuccessStatusCode)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await database.Migrate(); await database.Migrate();
        Assert.Equal("SUBMITTED", (await Body<WeeklyAssessmentDto>(await replacement.GetAsync($"/api/v1/weekly-assessments/{successor.Id}"))).Status);
    }
    private sealed class WeeklyFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        public bool FailAudit { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => {
                var existing = services.Single(d => d.ServiceType == typeof(IAuditTrail)); services.Remove(existing);
                services.AddScoped<IAuditTrail>(p => new WeeklyAudit((IAuditTrail)ActivatorUtilities.CreateInstance(p, existing.ImplementationType!), this));
            });
        }
    }
    private sealed class WeeklyAudit(IAuditTrail inner, WeeklyFactory app) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        { await inner.RecordAsync(entry, ct); if (app.FailAudit && entry.Action == "WEEKLY_ASSESSMENT_SUBMITTED") throw new InvalidOperationException("Synthetic weekly audit failure"); }
    }
}
