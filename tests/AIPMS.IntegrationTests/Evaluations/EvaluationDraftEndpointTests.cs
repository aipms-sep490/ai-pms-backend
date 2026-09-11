using System.Net;
using System.Data.Common;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class EvaluationDraftEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static string AssignUrl(long project) => $"/api/v1/projects/{project}/evaluation-assignments";
    private static string CreateUrl(long assignment) => $"/api/v1/evaluation-assignments/{assignment}/evaluation";
    private static string DraftUrl(long id) => $"/api/v1/evaluations/{id}";
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<EvaluationAssignmentDto> Assign(HttpClient staff, EvaluationScenario s, long? user = null, string type = "LECTURER") =>
        await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(user ?? s.Scope.Users.Lecturer, s.PeriodId, type)));
    private static async Task<EvaluationDraftDto> Create(HttpClient lecturer, long assignment) =>
        await Body<EvaluationDraftDto>(await lecturer.PostAsync(CreateUrl(assignment), null));
    private static SaveEvaluationDraftRequest Input(EvaluationDraftDto draft, EvaluationScenario s) =>
        new(draft.ConcurrencyToken, "Overall comment", [new(s.Criteria[0], 9, "Good design"), new(s.Criteria[1], 16, "Clear")]);
    private static Task<HttpResponseMessage> Save(HttpClient client, EvaluationDraftDto draft, EvaluationScenario s) =>
        client.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft", Input(draft, s));

    [Fact]
    public async Task Assigned_lecturer_saves_partial_complete_and_cleared_drafts_with_deterministic_preview()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        Assert.Equal(s.RubricId, assignment.RubricId);
        var mine = await Body<PagedResult<EvaluationAssignmentDto>>(await lecturer.GetAsync("/api/v1/evaluation-assignments/my"));
        Assert.Equal(assignment.Id, Assert.Single(mine.Items).Id);
        var draft = await Create(lecturer, assignment.Id);
        Assert.Equal("Evaluation rubric", draft.RubricName);
        Assert.Equal(1, draft.RubricVersion);
        Assert.Null(draft.TotalScore);
        Assert.Equal(s.Criteria, draft.MissingCriterionIds);
        Assert.Equal(new[] { s.Criteria[0] }, draft.MissingRequiredCriterionIds);
        var partial = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Partial", [new(s.Criteria[0], 9, "Good")])));
        Assert.Null(partial.TotalScore);
        Assert.Empty(partial.MissingRequiredCriterionIds);
        Assert.Equal(new[] { s.Criteria[1] }, partial.MissingCriterionIds);
        var completed = await Body<EvaluationDraftDto>(await Save(lecturer, partial, s));
        Assert.Equal(8.6m, completed.TotalScore);
        Assert.Equal(10m, completed.ScoreScale);
        Assert.Equal("DRAFT", completed.Status);
        Assert.NotEqual(partial.ConcurrencyToken, completed.ConcurrencyToken);
        Assert.Equal("Good design", completed.Scores[0].Comments);
        Assert.Equal(DateTimeKind.Utc, completed.UpdatedAt.Kind);
        var cleared = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft",
            new SaveEvaluationDraftRequest(completed.ConcurrencyToken, "Clear", [])));
        Assert.Null(cleared.TotalScore);
        Assert.All(cleared.Scores, score => Assert.Null(score.Score));
        var staffList = await Body<PagedResult<EvaluationDraftDto>>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/evaluations"));
        Assert.Equal(draft.Id, Assert.Single(staffList.Items).Id);
        await using var db = database.CreateContext();
        Assert.False(await db.EvaluationDetails.AnyAsync(d => d.EvaluationId == draft.Id));
        var row = await db.Evaluations.SingleAsync(e => e.Id == draft.Id);
        Assert.Null(row.EvaluatedAt);
        Assert.Equal("DRAFT", row.Status);
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Action == "EVALUATION_DRAFT_SAVED" && a.EntityId == draft.Id.ToString()));
    }

    [Fact]
    public async Task Staff_admin_students_and_unassigned_lecturers_cannot_write_another_evaluators_draft()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = await Create(lecturer, assignment.Id);
        foreach (var id in new[] { s.Scope.Users.Staff, s.Scope.Users.Admin, s.Scope.Users.Student, s.Scope.Users.NewLecturer, s.Scope.Users.OutsideStaff })
        {
            using var other = app.CreateAuthenticatedClient(id, roles: ["ADMIN", "LECTURER"]);
            Assert.Equal(HttpStatusCode.Forbidden, (await Save(other, draft, s)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsync(CreateUrl(assignment.Id), null)).StatusCode);
        }
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var outside = app.CreateAuthenticatedClient(s.Scope.Users.OutsideStaff);
        using var otherLecturer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(DraftUrl(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.GetAsync(DraftUrl(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherLecturer.GetAsync(DraftUrl(draft.Id))).StatusCode);
        Assert.Empty((await Body<PagedResult<EvaluationDraftDto>>(await otherLecturer.GetAsync($"/api/v1/projects/{s.ProjectId}/evaluations"))).Items);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(DraftUrl(draft.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outside.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
    }

    [Theory]
    [InlineData("SCORE_RANGE", 409)]
    [InlineData("NEGATIVE", 400)]
    [InlineData("PRECISION", 400)]
    [InlineData("FOREIGN_CRITERION", 409)]
    [InlineData("DUPLICATE", 400)]
    [InlineData("NULL_ITEM", 400)]
    [InlineData("NO_SCORE", 400)]
    public async Task Invalid_scores_return_problem_details_without_mutating_saved_data(string reason, int expected)
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var draft = await Create(lecturer, (await Assign(staff, s)).Id);
        var valid = Input(draft, s);
        EvaluationScoreInput[] scores = reason switch
        {
            "SCORE_RANGE" => [new(s.Criteria[0], 11, null)],
            "NEGATIVE" => [new(s.Criteria[0], -1, null)],
            "PRECISION" => [new(s.Criteria[0], 1.111m, null)],
            "FOREIGN_CRITERION" => [new(long.MaxValue, 5, null)],
            "DUPLICATE" => [valid.Scores[0], valid.Scores[0]],
            "NULL_ITEM" => [null!],
            _ => [new(s.Criteria[0], null, "Missing score")]
        };
        var response = await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft", valid with { Scores = scores });
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var after = await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)));
        Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Null(after.TotalScore);
    }

    [Theory]
    [InlineData("ACTIVE_PROJECT")]
    [InlineData("CLOSED_SEMESTER")]
    [InlineData("NOT_STARTED")]
    [InlineData("AT_END")]
    [InlineData("OVERLAP")]
    [InlineData("INACTIVE_PERIOD")]
    [InlineData("WRONG_PERIOD_TYPE")]
    public async Task Creation_and_saving_recheck_project_and_evaluation_window(string reason)
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = await Create(lecturer, assignment.Id);
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.PeriodId))!;
            if (reason == "ACTIVE_PROJECT") (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
            if (reason == "CLOSED_SEMESTER") (await db.AcademicSemesters.FindAsync(s.Scope.SemesterId))!.Status = "CLOSED";
            if (reason == "NOT_STARTED") period.StartAt = EvaluationDraftDatabaseFixture.Now.AddSeconds(1);
            if (reason == "AT_END") period.EndAt = EvaluationDraftDatabaseFixture.Now;
            if (reason == "INACTIVE_PERIOD") period.Status = "DRAFT";
            if (reason == "WRONG_PERIOD_TYPE") period.PeriodType = "EXECUTION";
            if (reason == "OVERLAP") db.ProjectPeriods.Add(new() { AcademicSemesterId = s.Scope.SemesterId,
                Code = Guid.NewGuid().ToString("N"), Name = "Overlap", PeriodType = "EVALUATION", Status = "ACTIVE",
                StartAt = period.StartAt, EndAt = period.EndAt, RubricId = s.RubricId });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Save(lecturer, draft, s)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lecturer.PostAsync(CreateUrl(assignment.Id), null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER"))).StatusCode);
    }

    [Theory]
    [InlineData("DRAFT_RUBRIC")]
    [InlineData("RETIRED_RUBRIC")]
    [InlineData("WRONG_SEMESTER")]
    [InlineData("WRONG_DEPARTMENT")]
    [InlineData("NO_RUBRIC")]
    [InlineData("INVALID_CRITERIA")]
    [InlineData("UNSCOPED_RUBRIC")]
    public async Task Assignments_require_a_published_usable_rubric_from_the_period(string reason)
    {
        var s = await database.Seed();
        await using (var db = database.CreateContext())
        {
            var rubric = (await db.Rubrics.FindAsync(s.RubricId))!;
            var version = (await db.Set<RubricVersion>().FindAsync(s.RubricId))!;
            if (reason == "DRAFT_RUBRIC") { version.Status = "DRAFT"; rubric.IsActive = false; }
            if (reason == "RETIRED_RUBRIC") { version.Status = "RETIRED"; rubric.IsActive = false; }
            if (reason == "WRONG_SEMESTER") rubric.AcademicSemester = new() { OrganizationId = s.Scope.OrganizationId,
                Code = Guid.NewGuid().ToString("N"), Name = "Other", Status = "ACTIVE", StartDate = new(2026,1,1), EndDate = new(2026,12,31) };
            if (reason == "WRONG_DEPARTMENT") rubric.DepartmentId = (await db.Users.FindAsync(s.Scope.Users.OutsideStaff))!.DepartmentId;
            if (reason == "UNSCOPED_RUBRIC") rubric.DepartmentId = null;
            if (reason == "NO_RUBRIC") (await db.ProjectPeriods.FindAsync(s.PeriodId))!.RubricId = null;
            if (reason == "INVALID_CRITERIA") (await db.RubricCriteria.FindAsync(s.Criteria[0]))!.WeightPercent = 1;
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
    }

    [Theory]
    [InlineData("REVOKED")]
    [InlineData("ROLE_REMOVED")]
    [InlineData("INACTIVE_ACCOUNT")]
    [InlineData("DEPARTMENT_CHANGED")]
    [InlineData("SUPERVISION_ENDED")]
    public async Task Lost_assignment_or_persisted_eligibility_blocks_reads_and_writes(string reason)
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer, roles: ["LECTURER"]);
        var assignment = await Assign(staff, s, type: "SUPERVISOR");
        var draft = await Create(lecturer, assignment.Id);
        if (reason == "REVOKED") await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(
            $"/api/v1/evaluation-assignments/{assignment.Id}/revoke", new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Changed evaluator")));
        else
        {
            await using var db = database.CreateContext();
            if (reason == "ROLE_REMOVED") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Scope.Users.Lecturer).ToArrayAsync());
            if (reason == "INACTIVE_ACCOUNT") (await db.Users.FindAsync(s.Scope.Users.Lecturer))!.Status = "INACTIVE";
            if (reason == "DEPARTMENT_CHANGED") (await db.Users.FindAsync(s.Scope.Users.Lecturer))!.DepartmentId = (await db.Users.FindAsync(s.Scope.Users.OutsideStaff))!.DepartmentId;
            if (reason == "SUPERVISION_ENDED") (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId)).EndedAt = EvaluationDraftDatabaseFixture.Now;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(lecturer, draft, s)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(DraftUrl(draft.Id))).StatusCode);
        await Body<EvaluationDraftDto>(await staff.GetAsync(DraftUrl(draft.Id)));
    }

    [Fact]
    public async Task Retiring_rubric_and_changing_period_selection_preserve_the_existing_assignment_version()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var rubric = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{s.RubricId}"));
        var retired = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{s.RubricId}/retire", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        var next = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{s.RubricId}/versions",
            new CreateRubricVersionRequest("V_" + Guid.NewGuid().ToString("N"), retired.ConcurrencyToken)));
        await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{next.Id}/publish", new ChangeRubricStatusRequest(next.ConcurrencyToken)));
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(s.PeriodId))!.RubricId = next.Id;
            await db.SaveChangesAsync();
        }
        var draft = await Create(lecturer, assignment.Id);
        Assert.Equal(s.RubricId, draft.RubricId);
        Assert.Equal(s.Criteria, draft.Scores.Select(c => c.RubricCriterionId).ToArray());
        Assert.Equal(8.6m, (await Body<EvaluationDraftDto>(await Save(lecturer, draft, s))).TotalScore);
        Assert.Equal(next.Id, (await Assign(staff, s, s.Scope.Users.NewLecturer)).RubricId);
    }

    [Fact]
    public async Task Concurrent_assignment_creation_draft_creation_and_save_do_not_duplicate_or_lose_edits()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignmentReplies = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))));
        Assert.Single(assignmentReplies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(assignmentReplies, r => r.StatusCode == HttpStatusCode.Conflict);
        var assignment = await Body<EvaluationAssignmentDto>(assignmentReplies.Single(r => r.IsSuccessStatusCode));
        var createReplies = await Task.WhenAll(lecturer.PostAsync(CreateUrl(assignment.Id), null), lecturer.PostAsync(CreateUrl(assignment.Id), null));
        Assert.Single(createReplies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(createReplies, r => r.StatusCode == HttpStatusCode.Conflict);
        var draft = await Body<EvaluationDraftDto>(createReplies.Single(r => r.IsSuccessStatusCode));
        var saves = await Task.WhenAll(Save(lecturer, draft, s), lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft",
            Input(draft, s) with { Scores = [new(s.Criteria[0], 0, null), new(s.Criteria[1], 0, null)] }));
        Assert.Single(saves, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(saves, r => r.StatusCode == HttpStatusCode.Conflict);
        var saved = await Body<EvaluationDraftDto>(saves.Single(r => r.IsSuccessStatusCode));
        var read = await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)));
        Assert.Equal(saved.TotalScore, read.TotalScore);
        Assert.Equal(saved.ConcurrencyToken, read.ConcurrencyToken);
        Assert.Equal(saved.Scores, read.Scores);
    }

    [Theory]
    [InlineData("SUBMITTED")]
    [InlineData("FINALIZED")]
    public async Task Existing_non_draft_status_is_immutable(string status)
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = await Create(lecturer, assignment.Id);
        await using (var db = database.CreateContext())
        {
            (await db.Evaluations.FindAsync(draft.Id))!.Status = status;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Save(lecturer, draft, s)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
            new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Cannot revoke"))).StatusCode);
    }

    [Theory]
    [InlineData("ASSIGN")]
    [InlineData("CREATE")]
    [InlineData("SAVE")]
    [InlineData("REVOKE")]
    public async Task Audit_failure_rolls_back_assignment_scores_total_and_token(string action)
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var failingApp = new EvaluationFactory(database, true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var failingStaff = failingApp.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var failingLecturer = failingApp.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = action == "CREATE" ? null : await Create(lecturer, assignment.Id);
        var response = action switch
        {
            "ASSIGN" => await failingStaff.PostAsJsonAsync(AssignUrl(s.ProjectId), new AssignEvaluatorRequest(s.Scope.Users.NewLecturer, s.PeriodId, "LECTURER")),
            "CREATE" => await failingLecturer.PostAsync(CreateUrl(assignment.Id), null),
            "REVOKE" => await failingStaff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke", new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Failed revoke")),
            _ => await Save(failingLecturer, draft!, s)
        };
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Set<EvaluationAssignment>().CountAsync(a => a.ProjectId == s.ProjectId));
        Assert.Equal("ACTIVE", (await db.Set<EvaluationAssignment>().FindAsync(assignment.Id))!.Status);
        if (draft is null) Assert.False(await db.Evaluations.AnyAsync(e => e.ProjectId == s.ProjectId));
        else
        {
            var after = await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)));
            Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken);
            Assert.Null(after.TotalScore);
            Assert.All(after.Scores, score => Assert.Null(score.Score));
        }
    }

    [Fact]
    public async Task Migration_rerun_preserves_assignments_and_drafts()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = await Body<EvaluationDraftDto>(await Save(lecturer, await Create(lecturer, assignment.Id), s));
        await database.Migrate();
        await database.Migrate();
        var after = await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)));
        Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(draft.TotalScore, after.TotalScore);
        Assert.Equal(draft.Scores, after.Scores);
    }

    [Fact]
    public async Task Manager_cannot_assign_ineligible_people_or_forge_supervision_and_removed_staff_role_loses_access()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff, roles: ["DEPARTMENT_STAFF"]);
        foreach (var (user, type) in new[] { (s.Scope.Users.Student, "LECTURER"),
            (s.Scope.Users.OtherLecturer, "LECTURER"), (s.Scope.Users.NewLecturer, "SUPERVISOR") })
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
                new AssignEvaluatorRequest(user, s.PeriodId, type))).StatusCode);
        await using (var db = database.CreateContext())
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Scope.Users.Staff).ToListAsync());
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(AssignUrl(s.ProjectId),
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER"))).StatusCode);
    }

    [Fact]
    public async Task Window_start_is_inclusive_and_paginated_lists_do_not_expose_peer_evaluations()
    {
        var s = await database.Seed();
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(s.PeriodId))!.StartAt = EvaluationDraftDatabaseFixture.Now;
            await db.SaveChangesAsync();
        }
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var peer = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        var first = await Assign(staff, s);
        var second = await Assign(staff, s, s.Scope.Users.NewLecturer);
        var mine = await Create(lecturer, first.Id);
        var peerDraft = await Create(peer, second.Id);
        var mineList = await Body<PagedResult<EvaluationDraftDto>>(await lecturer.GetAsync($"/api/v1/projects/{s.ProjectId}/evaluations?pageSize=1"));
        Assert.Equal(1, mineList.TotalCount);
        Assert.Equal(mine.Id, Assert.Single(mineList.Items).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(DraftUrl(peerDraft.Id))).StatusCode);
        var staffPage = await Body<PagedResult<EvaluationDraftDto>>(await staff.GetAsync($"/api/v1/projects/{s.ProjectId}/evaluations?pageSize=1&page=2"));
        Assert.Equal(2, staffPage.TotalCount);
        Assert.Equal(mine.Id, Assert.Single(staffPage.Items).Id);
        var assignments = await Body<PagedResult<EvaluationAssignmentDto>>(await staff.GetAsync(AssignUrl(s.ProjectId) + "?pageSize=1"));
        Assert.Equal(2, assignments.TotalCount);
        Assert.Equal(second.Id, Assert.Single(assignments.Items).Id);
    }

    [Fact]
    public async Task Concurrent_save_and_revoke_preserve_history_and_block_further_writes()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        var draft = await Create(lecturer, assignment.Id);
        var revokePath = $"/api/v1/evaluation-assignments/{assignment.Id}/revoke";
        var revoke = new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Reassign for review");
        var responses = await Task.WhenAll(Save(lecturer, draft, s), staff.PostAsJsonAsync(revokePath, revoke));
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Forbidden });
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        if (!responses[1].IsSuccessStatusCode) await Body<EvaluationAssignmentDto>(await staff.PostAsJsonAsync(revokePath, revoke));
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(lecturer, draft, s)).StatusCode);
        Assert.Empty((await Body<PagedResult<EvaluationAssignmentDto>>(await lecturer.GetAsync("/api/v1/evaluation-assignments/my"))).Items);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Evaluations.CountAsync(e => e.Id == draft.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "EVALUATOR_REVOKED" && a.EntityId == assignment.Id.ToString()));
    }

    [Fact]
    public async Task A_finalization_committed_before_score_read_cannot_be_overwritten_by_the_draft_request()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var draft = await Create(lecturer, (await Assign(staff, s)).Id);
        var gate = new PauseBeforeEvaluationRead();
        using var paused = new EvaluationFactory(database, interceptor: gate);
        using var pausedLecturer = paused.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var pending = Save(pausedLecturer, draft, s);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var db = database.CreateContext();
            await db.Evaluations.Where(e => e.Id == draft.Id).ExecuteUpdateAsync(set => set
                .SetProperty(e => e.Status, "FINALIZED").SetProperty(e => e.TotalScore, 7.25m));
        }
        finally { gate.Resume.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        await using var verify = database.CreateContext();
        var persisted = await verify.Evaluations.SingleAsync(e => e.Id == draft.Id);
        Assert.Equal("FINALIZED", persisted.Status);
        Assert.Equal(7.25m, persisted.TotalScore);
        Assert.False(await verify.EvaluationDetails.AnyAsync(d => d.EvaluationId == draft.Id));
    }
}

internal sealed class PauseBeforeEvaluationRead : DbCommandInterceptor
{
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM [evaluations]", StringComparison.Ordinal))
        {
            Reached.TrySetResult();
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        return result;
    }
}

internal sealed class EvaluationFactory(EvaluationDraftDatabaseFixture database, bool failAudit = false,
    DbCommandInterceptor? interceptor = null) : AipmsWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
        builder.ConfigureServices(services =>
        {
            if (interceptor is not null)
            {
                services.RemoveAll<DbContextOptions<AipmsDbContext>>();
                services.AddDbContext<AipmsDbContext>(options => options.UseSqlServer(database.ConnectionString).AddInterceptors(interceptor));
            }
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new EvaluationClock());
            if (failAudit)
            {
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail, EvaluationFailingAudit>();
            }
        });
    }
    private sealed class EvaluationClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(EvaluationDraftDatabaseFixture.Now);
    }
    private sealed class EvaluationFailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected evaluation audit failure");
    }
}
