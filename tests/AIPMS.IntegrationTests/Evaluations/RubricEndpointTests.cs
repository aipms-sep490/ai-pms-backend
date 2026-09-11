using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class RubricEndpointTests(RubricDatabaseFixture database) : IClassFixture<RubricDatabaseFixture>
{
    private static readonly RubricCriterionInput[] ValidCriteria =
        [new("Design", "Design evidence", 60m, 10m, 0, true), new("Presentation", null, 40m, 20m, 1, false)];
    private static CreateRubricRequest Input(RubricScenario s) => new(s.Users.DepartmentId, s.SemesterId,
        "R_" + Guid.NewGuid().ToString("N"), "Rubric", null, ValidCriteria);
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<RubricDto> Create(HttpClient client, RubricScenario s) => Create(client, Input(s));
    private static async Task<RubricDto> Create(HttpClient client, CreateRubricRequest input) =>
        await Body<RubricDto>(await client.PostAsJsonAsync("/api/v1/rubrics", input));
    private static async Task<RubricDto> Status(HttpClient client, RubricDto rubric, string action) =>
        await Body<RubricDto>(await client.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/{action}", new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
    private static UpdateRubricRequest Edit(RubricDto rubric, string name = "Updated") => new(name, "Description", ValidCriteria, rubric.ConcurrencyToken);

    [Fact]
    public async Task Draft_edit_publish_retire_and_new_version_preserve_published_content_and_BE12_contract()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        var draft = await Create(staff, s);
        Assert.Equal("DRAFT", draft.Status);
        Assert.False(draft.IsActive);
        Assert.True(draft.CanEdit);
        Assert.Equal(draft.Id, draft.RootRubricId);
        Assert.Equal(1, draft.Version);
        Assert.False(draft.Criteria[1].IsRequired);
        using var scope = app.Services.CreateScope();
        var periods = scope.ServiceProvider.GetRequiredService<ISemesterRepository>();
        Assert.False(await periods.ValidateRubricUsableAsync(draft.Id, s.SemesterId));
        var edited = await Body<RubricDto>(await staff.PutAsJsonAsync($"/api/v1/rubrics/{draft.Id}",
            Edit(draft) with { Criteria = [ValidCriteria[1] with { SortOrder = 0 }, ValidCriteria[0] with { SortOrder = 1 }] }));
        Assert.NotEqual(draft.ConcurrencyToken, edited.ConcurrencyToken);
        Assert.Equal("Presentation", edited.Criteria[0].Name);
        var published = await Status(staff, edited, "publish");
        Assert.True(published.IsActive);
        Assert.False(published.CanEdit);
        Assert.True(await periods.ValidateRubricUsableAsync(published.Id, s.SemesterId));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{published.Id}", Edit(published))).StatusCode);
        var retired = await Status(staff, published, "retire");
        Assert.False(await periods.ValidateRubricUsableAsync(retired.Id, s.SemesterId));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{retired.Id}", Edit(retired))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{retired.Id}/publish", new ChangeRubricStatusRequest(retired.ConcurrencyToken))).StatusCode);
        var next = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{retired.Id}/versions",
            new CreateRubricVersionRequest("V_" + Guid.NewGuid().ToString("N"), retired.ConcurrencyToken)));
        Assert.Equal(2, next.Version);
        Assert.Equal(draft.Id, next.RootRubricId);
        Assert.NotEqual(draft.Id, next.Id);
        Assert.Equal("DRAFT", next.Status);
        Assert.Empty(next.Criteria.Select(c => c.CriterionId).Intersect(retired.Criteria.Select(c => c.CriterionId)));
        await Body<RubricDto>(await staff.PutAsJsonAsync($"/api/v1/rubrics/{next.Id}", Edit(next)));
        var old = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{retired.Id}"));
        Assert.Equal(retired.Criteria, old.Criteria);
        await using var db = database.CreateContext();
        Assert.Equal(6, await db.AuditLogs.CountAsync(a => a.EntityType == "RUBRIC" &&
            (a.EntityId == draft.Id.ToString() || a.EntityId == next.Id.ToString())));
    }

    [Fact]
    public async Task Scoped_listing_filter_pagination_and_persisted_roles_prevent_cross_department_access()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff, roles: [AppRoles.DepartmentStaff]);
        using var other = app.CreateAuthenticatedClient(s.Users.OutsideStaff, roles: [AppRoles.DepartmentStaff]);
        using var student = app.CreateAuthenticatedClient(s.Users.Student, roles: [AppRoles.Admin]);
        using var anonymous = app.CreateClient();
        var first = await Create(staff, s);
        var second = await Create(staff, s);
        var page = await Body<PagedResult<RubricDto>>(await staff.GetAsync($"/api/v1/rubrics?academicSemesterId={s.SemesterId}&status=DRAFT&pageSize=1"));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        var searched = await Body<PagedResult<RubricDto>>(await staff.GetAsync($"/api/v1/rubrics?search={first.Code}"));
        Assert.Equal(first.Id, Assert.Single(searched.Items).Id);
        Assert.Empty((await Body<PagedResult<RubricDto>>(await other.GetAsync("/api/v1/rubrics"))).Items);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/rubrics/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/v1/rubrics/{first.Id}", Edit(first))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/rubrics/{first.Id}/publish", new ChangeRubricStatusRequest(first.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/rubrics/{first.Id}/retire", new ChangeRubricStatusRequest(first.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/rubrics/{first.Id}/versions", new CreateRubricVersionRequest("OUTSIDE", first.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/rubrics/{first.Id}?concurrencyToken={first.ConcurrencyToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/v1/rubrics", Input(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/rubrics")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/rubrics")).StatusCode);
        await using var db = database.CreateContext();
        db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Users.Staff).ToListAsync());
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/rubrics/{first.Id}")).StatusCode);
    }

    [Theory]
    [InlineData("CLOSED")]
    [InlineData("ARCHIVED")]
    [InlineData("INACTIVE_DEPARTMENT")]
    [InlineData("INACTIVE_ORGANIZATION")]
    [InlineData("OTHER_ORGANIZATION")]
    public async Task Creating_requires_active_consistent_academic_scope(string reason)
    {
        var s = await database.Seed();
        await using (var db = database.CreateContext())
        {
            if (reason is "CLOSED" or "ARCHIVED") (await db.AcademicSemesters.FindAsync(s.SemesterId))!.Status = reason;
            else if (reason == "INACTIVE_DEPARTMENT") (await db.Departments.FindAsync(s.Users.DepartmentId))!.IsActive = false;
            else if (reason == "INACTIVE_ORGANIZATION") (await db.Organizations.FindAsync(s.OrganizationId))!.IsActive = false;
            else (await db.AcademicSemesters.FindAsync(s.SemesterId))!.Organization = new M.Organization { Code = Guid.NewGuid().ToString("N"), Name = "Other", IsActive = true };
            await db.SaveChangesAsync();
        }
        using var app = new RubricFactory(database);
        using var admin = app.CreateAuthenticatedClient(s.Users.Admin, roles: [AppRoles.Admin]);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/rubrics", Input(s))).StatusCode);
    }

    [Theory]
    [InlineData("EMPTY")]
    [InlineData("SUM")]
    [InlineData("NO_REQUIRED")]
    public async Task Incomplete_drafts_are_saved_but_cannot_be_published(string reason)
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var criteria = reason switch { "EMPTY" => Array.Empty<RubricCriterionInput>(),
            "SUM" => [ValidCriteria[0]], _ => ValidCriteria.Select(c => c with { IsRequired = false }).ToArray() };
        var rubric = await Create(staff, Input(s) with { Criteria = criteria });
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish",
            new ChangeRubricStatusRequest(rubric.ConcurrencyToken))).StatusCode);
        Assert.Equal("DRAFT", (await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{rubric.Id}"))).Status);
    }

    [Fact]
    public async Task Invalid_criterion_precision_and_missing_concurrency_token_return_problem_details()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var response = await staff.PostAsJsonAsync("/api/v1/rubrics", Input(s) with { Criteria = [ValidCriteria[0] with { MaxScore = 1.001m }] });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var rubric = await Create(staff, s);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubric.Id}", Edit(rubric) with { ConcurrencyToken = "" })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_creates_and_edits_have_conflicts_instead_of_duplicates_or_lost_updates()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var input = Input(s);
        var creates = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => staff.PostAsJsonAsync("/api/v1/rubrics", input)));
        Assert.Single(creates, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(2, creates.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var draft = await Body<RubricDto>(creates.Single(r => r.IsSuccessStatusCode));
        var changes = await Task.WhenAll(staff.PutAsJsonAsync($"/api/v1/rubrics/{draft.Id}", Edit(draft)),
            staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish", new ChangeRubricStatusRequest(draft.ConcurrencyToken)));
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(changes, r => r.StatusCode == HttpStatusCode.Conflict);
        var current = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{draft.Id}"));
        Assert.NotEqual(draft.ConcurrencyToken, current.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{draft.Id}", Edit(draft))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_new_versions_get_distinct_ordered_numbers_and_independent_criteria()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var source = await Status(staff, await Create(staff, s), "publish");
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => staff.PostAsJsonAsync($"/api/v1/rubrics/{source.Id}/versions",
            new CreateRubricVersionRequest("V_" + Guid.NewGuid().ToString("N"), source.ConcurrencyToken))));
        var versions = await Task.WhenAll(responses.Select(Body<RubricDto>));
        Assert.Equal(new[] { 2, 3, 4 }, versions.Select(v => v.Version).Order().ToArray());
        Assert.Equal(6, versions.SelectMany(v => v.Criteria.Select(c => c.CriterionId)).Distinct().Count());
    }

    [Theory]
    [InlineData("CREATE")]
    [InlineData("UPDATE")]
    [InlineData("PUBLISH")]
    [InlineData("DELETE")]
    public async Task Audit_failure_rolls_back_entire_mutation(string action)
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var before = await Create(staff, s);
        using var failingApp = new RubricFactory(database, true);
        using var failing = failingApp.CreateAuthenticatedClient(s.Users.Staff);
        var input = Input(s);
        var response = action switch
        {
            "CREATE" => await failing.PostAsJsonAsync("/api/v1/rubrics", input),
            "UPDATE" => await failing.PutAsJsonAsync($"/api/v1/rubrics/{before.Id}", Edit(before)),
            "DELETE" => await failing.DeleteAsync($"/api/v1/rubrics/{before.Id}?concurrencyToken={before.ConcurrencyToken}"),
            _ => await failing.PostAsJsonAsync($"/api/v1/rubrics/{before.Id}/publish", new ChangeRubricStatusRequest(before.ConcurrencyToken))
        };
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var after = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{before.Id}"));
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(before.Criteria, after.Criteria);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Status, after.Status);
        await using var db = database.CreateContext();
        Assert.False(await db.Rubrics.AnyAsync(r => r.Code == input.Code));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityType == "RUBRIC" && a.EntityId == before.Id.ToString()));
    }

    [Fact]
    public async Task Only_unreferenced_drafts_can_be_deleted()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var draft = await Create(staff, s);
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"/api/v1/rubrics/{draft.Id}?concurrencyToken={draft.ConcurrencyToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/rubrics/{draft.Id}")).StatusCode);
        var published = await Status(staff, await Create(staff, s), "publish");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.DeleteAsync($"/api/v1/rubrics/{published.Id}?concurrencyToken={published.ConcurrencyToken}")).StatusCode);
    }

    [Fact]
    public async Task An_existing_reference_protects_even_a_draft_and_new_versions_do_not_rebind_periods()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var draft = await Create(staff, s);
        var published = await Status(staff, await Create(staff, s), "publish");
        long periodId;
        await using (var db = database.CreateContext())
        {
            M.ProjectPeriod Period(long rubricId) => new() { AcademicSemesterId = s.SemesterId,
                Code = Guid.NewGuid().ToString("N"), Name = "Evaluation", PeriodType = "EVALUATION", Status = "DRAFT",
                StartAt = new(2026, 10, 1), EndAt = new(2026, 10, 31), RubricId = rubricId };
            var period = Period(published.Id);
            db.ProjectPeriods.AddRange(period, Period(draft.Id));
            await db.SaveChangesAsync();
            periodId = period.Id;
        }
        Assert.False((await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{draft.Id}"))).CanEdit);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{draft.Id}", Edit(draft))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish", new ChangeRubricStatusRequest(draft.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.DeleteAsync($"/api/v1/rubrics/{draft.Id}?concurrencyToken={draft.ConcurrencyToken}")).StatusCode);
        var retired = await Status(staff, published, "retire");
        await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{retired.Id}/versions",
            new CreateRubricVersionRequest("V_" + Guid.NewGuid().ToString("N"), retired.ConcurrencyToken)));
        await using var verify = database.CreateContext();
        Assert.Equal(published.Id, (await verify.ProjectPeriods.FindAsync(periodId))!.RubricId);
    }

    [Fact]
    public async Task Publishing_rechecks_semester_state_and_migration_does_not_reset_managed_drafts()
    {
        var s = await database.Seed();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var draft = await Create(staff, s);
        await database.Migrate();
        var after = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{draft.Id}"));
        Assert.Equal("DRAFT", after.Status);
        Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken);
        await using (var db = database.CreateContext())
        {
            (await db.AcademicSemesters.FindAsync(s.SemesterId))!.Status = "CLOSED";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/rubrics/{draft.Id}/publish", new ChangeRubricStatusRequest(draft.ConcurrencyToken))).StatusCode);
    }

    [Fact]
    public async Task Migration_is_repeatable_preserves_legacy_evaluation_and_freezes_inactive_legacy_versions()
    {
        var s = await database.Seed();
        long rubricId, evaluationId;
        await using (var db = database.CreateContext())
        {
            var rubric = new M.Rubric { Code = "LEGACY_" + Guid.NewGuid().ToString("N"), Name = "Old rubric",
                DepartmentId = s.Users.DepartmentId, AcademicSemesterId = s.SemesterId, IsActive = false, CreatedBy = s.Users.Staff,
                RubricCriteria = [new() { WeightPercent = 100, MaxScore = 10, IsRequired = true,
                    Criterion = new() { Code = Guid.NewGuid().ToString("N"), Name = "Old criterion", IsActive = true } }] };
            var evaluation = new M.Evaluation { Rubric = rubric, EvaluatorId = s.Users.Lecturer, Status = "FINALIZED",
                EvaluationType = "FINAL", TotalScore = 8, Project = new() { Code = Guid.NewGuid().ToString("N"), Title = "Project",
                    Status = "ACTIVE", CreatedBy = s.Users.Student, Team = new() { AcademicSemesterId = s.SemesterId,
                        Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "LOCKED", CreatedBy = s.Users.Student } },
                EvaluationDetails = [new() { RubricCriterion = rubric.RubricCriteria.Single(), Score = 8, Comments = "Original" }] };
            db.Evaluations.Add(evaluation);
            await db.SaveChangesAsync();
            rubricId = rubric.Id;
            evaluationId = evaluation.Id;
        }
        await database.Migrate();
        using var app = new RubricFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Users.Staff);
        var legacy = await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{rubricId}"));
        Assert.Equal("RETIRED", legacy.Status);
        await database.Migrate();
        Assert.Equal(legacy.ConcurrencyToken, (await Body<RubricDto>(await staff.GetAsync($"/api/v1/rubrics/{rubricId}"))).ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}", Edit(legacy))).StatusCode);
        var next = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubricId}/versions",
            new CreateRubricVersionRequest("NEW_" + Guid.NewGuid().ToString("N"), legacy.ConcurrencyToken)));
        await Body<RubricDto>(await staff.PutAsJsonAsync($"/api/v1/rubrics/{next.Id}", Edit(next)));
        await using var verify = database.CreateContext();
        var oldEvaluation = await verify.Evaluations.Include(e => e.EvaluationDetails).ThenInclude(d => d.RubricCriterion).ThenInclude(c => c.Criterion)
            .SingleAsync(e => e.Id == evaluationId);
        Assert.Equal(rubricId, oldEvaluation.RubricId);
        Assert.Equal(8m, oldEvaluation.TotalScore);
        Assert.Equal("Old criterion", oldEvaluation.EvaluationDetails.Single().RubricCriterion.Criterion.Name);
        Assert.Equal(100m, oldEvaluation.EvaluationDetails.Single().RubricCriterion.WeightPercent);
    }
}

internal sealed class RubricFactory(RubricDatabaseFixture database, bool failAudit = false) : AipmsWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString }));
        if (failAudit) builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAuditTrail>();
            services.AddScoped<IAuditTrail, FailingRubricAudit>();
        });
    }
    private sealed class FailingRubricAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected rubric audit failure.");
    }
}
