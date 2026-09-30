using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Publishing_successor_does_not_extend_an_expired_predecessor_across_a_gap()
    {
        var (s, _) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var url = $"/api/v1/project-periods/{s.PeriodId}";
        var current = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"));
        var expiredAt = EvaluationDraftDatabaseFixture.Now.AddMinutes(-1);
        await using (var db = database.CreateContext())
        {
            (await db.Set<PeriodPolicyVersion>().FindAsync(current.Id))!.EffectiveTo = expiredAt;
            await db.SaveChangesAsync();
        }
        var request = new UpdatePeriodPolicyRequest(current.Version, "SUCCESSOR", current.Policy,
            new(EvaluationDraftDatabaseFixture.Now), new(EvaluationDraftDatabaseFixture.Now.AddHours(1)));
        var draft = await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", request));
        await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", request with {
            ExpectedVersion = draft.Version, Operation = "PUBLISH", ConcurrencyToken = draft.ConcurrencyToken }));
        var history = await Body<PeriodPolicyDto[]>(await staff.GetAsync(url + "/policy-versions"));
        Assert.Equal(expiredAt, history.Single(p => p.Id == current.Id).EffectiveTo);
        var gap = Uri.EscapeDataString(EvaluationDraftDatabaseFixture.Now.AddSeconds(-30).ToString("O"));
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(url + "/effective-policy?asOf=" + gap)).StatusCode);
    }

    [Fact]
    public async Task Scheme_rechecks_verified_department_and_individual_assignment_rechecks_verification()
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var input = Input(s, major);
        await using (var db = database.CreateContext())
        {
            var student = (await db.Users.FindAsync(s.Scope.Users.Student))!;
            student.DepartmentId = (await db.Users.FindAsync(s.Scope.Users.OutsideStaff))!.DepartmentId;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", input)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.Scope.Users.Student))!.DepartmentId = s.Scope.Users.DepartmentId;
            await db.SaveChangesAsync();
        }
        var scheme = await Scheme(staff, s, major);
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.Scope.Users.Student))!.AcademicProfileStatus = "REJECTED";
            await db.SaveChangesAsync();
        }
        var component = scheme.Components.Single(c => c.Scope == "INDIVIDUAL");
        var response = await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", component.Scope, major,
                s.Scope.Users.Student, component.Id));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Scheme_draft_edits_are_atomic_token_protected_and_published_versions_are_immutable()
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var outside = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        var input = Input(s, major);
        var draft = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes", input));
        var url = $"/api/v1/evaluation-schemes/{draft.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(url)).StatusCode);
        var edit = input with { Name = "Revised", ConcurrencyToken = draft.ConcurrencyToken };
        using (var fail = new EvaluationFactory(database, failAudit: true))
        using (var failing = fail.CreateAuthenticatedClient(s.Scope.Users.Staff))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PutAsJsonAsync(url, edit)).StatusCode);
        Assert.Equal(draft.ConcurrencyToken, (await Body<EvaluationSchemeDto>(await staff.GetAsync(url))).ConcurrencyToken);
        var replies = await Task.WhenAll(staff.PutAsJsonAsync(url, edit), staff.PostAsJsonAsync(url + "/publish", new SchemeTokenRequest(draft.ConcurrencyToken)));
        Assert.Single(replies, r => r.IsSuccessStatusCode); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        var latest = await Body<EvaluationSchemeDto>(await staff.GetAsync(url));
        if (latest.Status == "DRAFT") latest = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(url + "/publish", new SchemeTokenRequest(latest.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.DeleteAsync(url + "?concurrencyToken=" + latest.ConcurrencyToken)).StatusCode);
        var copy = await Body<EvaluationSchemeDto>(await staff.PostAsJsonAsync(url + "/versions", new SchemeTokenRequest(latest.ConcurrencyToken)));
        Assert.Equal(latest.Version + 1, copy.Version);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/evaluation-schemes/{copy.Id}?concurrencyToken={copy.ConcurrencyToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/evaluation-schemes/{copy.Id}")).StatusCode);
    }

    [Fact]
    public async Task Policy_reference_cannot_be_retroactively_replaced_and_legacy_period_crud_cannot_bypass_lock()
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var outside = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        await Scheme(staff, s, major);
        var url = $"/api/v1/project-periods/{s.PeriodId}";
        var before = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"));
        Assert.Equal("LOCKED", before.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(url + "/effective-policy")).StatusCode);
        var request = new UpdatePeriodPolicyRequest(before.Version, "SUCCESSOR", before.Policy with { MaxProjectsPerSupervisor = 8 },
            new(EvaluationDraftDatabaseFixture.Now), new(EvaluationDraftDatabaseFixture.Now.AddHours(12)));
        var draft = await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", request));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(url + "/policy", request with {
            ExpectedVersion = draft.Version, Operation = "PUBLISH", ConcurrencyToken = draft.ConcurrencyToken })).StatusCode);
        var history = await Body<PeriodPolicyDto[]>(await staff.GetAsync(url + "/policy-versions"));
        Assert.Equal(2, history.Length); Assert.Equal("DRAFT", history[0].Status);
        await using var db = database.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var repository = new AIPMS.Infrastructure.Persistence.Repositories.SemesterRepository(db);
        await Assert.ThrowsAsync<AIPMS.Application.Common.Exceptions.ConflictException>(() => repository.SetProjectPeriodGovernanceAsync(
            s.PeriodId, "SINGLE_MAJOR", "STUDENT_PROPOSAL", default));
    }

    private async Task RerunMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
        var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "db", "changes", "20260930_add_policy_evaluation_schemes.sql"));
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { MultipleActiveResultSets = false }.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await new SqlCommand(batch, connection).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Legacy_drafts_are_read_only_and_published_result_survives_migration_rerun()
    {
        var (s, _) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        long assignmentId, evaluationId;
        await using (var db = database.CreateContext())
        {
            var assignment = new EvaluationAssignment { ProjectId = s.ProjectId, EvaluatorId = s.Scope.Users.Lecturer,
                RubricId = s.RubricId, ProjectPeriodId = s.PeriodId, DepartmentId = s.Scope.Users.DepartmentId,
                EvaluationType = "LECTURER", AssignedBy = s.Scope.Users.Staff, AssignedAt = EvaluationDraftDatabaseFixture.Now, ConcurrencyToken = Guid.NewGuid() };
            var evaluation = new AIPMS.Infrastructure.Persistence.Generated.Models.Evaluation { ProjectId = s.ProjectId,
                EvaluatorId = s.Scope.Users.Lecturer, RubricId = s.RubricId, EvaluationType = "LECTURER", Status = "DRAFT" };
            db.Add(assignment); db.Evaluations.Add(evaluation); await db.SaveChangesAsync();
            db.Add(new EvaluationDraftState { EvaluationId = evaluation.Id, AssignmentId = assignment.Id, ConcurrencyToken = Guid.NewGuid() });
            var package = await db.Set<FinalSubmission>().SingleAsync(f => f.ProjectId == s.ProjectId);
            var result = new ProjectResultDto(0, s.ProjectId, package.Id, 7.25m, 5, "PASSED", "LEGACY", s.Scope.Users.Staff,
                EvaluationDraftDatabaseFixture.Now, "legacy-policy", []);
            db.Add(new ProjectResult { ProjectId = s.ProjectId, FinalSubmissionId = package.Id, PublishedBy = s.Scope.Users.Staff,
                PublishedAt = EvaluationDraftDatabaseFixture.Now, SnapshotJson = JsonSerializer.Serialize(result) });
            await db.SaveChangesAsync(); assignmentId = assignment.Id; evaluationId = evaluation.Id;
        }
        await RerunMigration(); await RerunMigration();
        var draft = await Body<EvaluationDraftDto>(await lecturer.GetAsync($"/api/v1/evaluations/{evaluationId}"));
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.PostAsync($"/api/v1/evaluation-assignments/{assignmentId}/evaluation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.PutAsJsonAsync($"/api/v1/evaluations/{evaluationId}/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, null, [new(s.Criteria[0], 8, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.PostAsJsonAsync($"/api/v1/evaluations/{evaluationId}/finalize", new { draft.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/result-policy",
            new ConfigureResultPolicyRequest(5, [new(assignmentId, 100)], null))).StatusCode);
        Assert.Equal(7.25m, (await Body<ProjectResultDto>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/result"))).TotalScore);
        await using var verify = database.CreateContext();
        Assert.Equal("UNKNOWN", (await verify.Set<EvaluationAssignment>().FindAsync(assignmentId))!.Scope);
        Assert.False(await verify.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == evaluationId));
    }

    [Fact]
    public async Task Concurrent_policy_writers_preserve_one_successor_and_half_open_intervals()
    {
        var (s, _) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var url = $"/api/v1/project-periods/{s.PeriodId}";
        var before = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"));
        var input = new UpdatePeriodPolicyRequest(before.Version, "SUCCESSOR", before.Policy with { MaxProjectsPerSupervisor = 7 },
            new(EvaluationDraftDatabaseFixture.Now), new(EvaluationDraftDatabaseFixture.Now.AddHours(12)));
        var replies = await Task.WhenAll(staff.PutAsJsonAsync(url + "/policy", input), staff.PutAsJsonAsync(url + "/policy", input));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        var draft = await Body<PeriodPolicyDto>(replies.Single(r => r.IsSuccessStatusCode));
        var publish = input with { ExpectedVersion = draft.Version, Operation = "PUBLISH", ConcurrencyToken = draft.ConcurrencyToken };
        await Body<PeriodPolicyDto>(await staff.PutAsJsonAsync(url + "/policy", publish));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(url + "/policy", publish)).StatusCode);
        var prior = await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy?asOf=" + Uri.EscapeDataString(EvaluationDraftDatabaseFixture.Now.AddTicks(-1).ToString("O"))));
        Assert.Equal(before.Id, prior.Id);
        Assert.Equal(draft.Id, (await Body<PeriodPolicyDto>(await staff.GetAsync(url + "/effective-policy"))).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(url + "/effective-policy?asOf=" + Uri.EscapeDataString(input.EffectiveTo.ToString("O")))).StatusCode);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("notification")]
    public async Task Student_publication_rolls_back_all_side_effects_and_retry_is_unique(string failure)
    {
        var (s, major) = await Seed(); using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var scheme = await Scheme(staff, s, major);
        await Evaluate(staff, lecturer, s, scheme.Components[0], 8);
        await Evaluate(staff, lecturer, s, scheme.Components[1], 6);
        var url = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(url + "/preview"));
        using (var failing = new FinalizationFailureFactory(database, failure))
        using (var failingStaff = failing.CreateAuthenticatedClient(s.Scope.Users.Staff))
            Assert.Equal(HttpStatusCode.InternalServerError, (await failingStaff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken))).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.False(await db.Set<StudentResult>().AnyAsync(r => r.ProjectId == s.ProjectId));
            Assert.False(await db.Notifications.AnyAsync(n => n.NotificationType == "STUDENT_RESULT_PUBLISHED" && n.CreatedBy == s.Scope.Users.Staff));
        }
        var replies = await Task.WhenAll(staff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken)), staff.PostAsJsonAsync(url, new PublishProjectResultRequest(preview.ConfirmationToken)));
        Assert.Single(replies, r => r.IsSuccessStatusCode); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        await RerunMigration(); await RerunMigration();
        await using var verify = database.CreateContext();
        Assert.Single(await verify.Set<StudentResult>().Where(r => r.ProjectId == s.ProjectId).ToListAsync());
    }
}
