using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    private static Task<HttpResponseMessage> Finalize(HttpClient client, EvaluationDraftDto draft) =>
        client.PostAsJsonAsync(DraftUrl(draft.Id) + "/finalize", new FinalizeEvaluationRequest(draft.ConcurrencyToken));
    private async Task<(EvaluationScenario Scenario, EvaluationAssignmentDto Assignment, EvaluationDraftDto Draft)> Prepared(EvaluationFactory app)
    {
        var s = await database.Seed();
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var assignment = await Assign(staff, s);
        await Body<AIPMS.Application.Features.Results.DTOs.ResultPolicyDto>(await staff.PutAsJsonAsync(
            $"/api/v1/projects/{s.ProjectId}/result-policy",
            new AIPMS.Application.Features.Results.DTOs.ConfigureResultPolicyRequest(5m, [new(assignment.Id, 100m)], null)));
        var draft = await Create(lecturer, assignment.Id);
        return (s, assignment, await Body<EvaluationDraftDto>(await Save(lecturer, draft, s)));
    }

    [Fact]
    public async Task Finalize_preserves_scores_evidence_and_blocks_edit_revoke_and_replay()
    {
        using var app = new EvaluationFactory(database);
        var (s, assignment, draft) = await Prepared(app);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        long[] detailIds;
        await using (var db = database.CreateContext())
        {
            detailIds = await db.EvaluationDetails.Where(d => d.EvaluationId == draft.Id).OrderBy(d => d.Id).Select(d => d.Id).ToArrayAsync();
            (await db.Evaluations.FindAsync(draft.Id))!.TotalScore = 1m;
            await db.SaveChangesAsync();
        }
        var finalized = await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        Assert.Equal("FINALIZED", finalized.Status);
        Assert.Equal(8.6m, finalized.TotalScore);
        Assert.NotEqual(draft.ConcurrencyToken, finalized.ConcurrencyToken);
        Assert.Equal(s.Scope.Users.Lecturer, finalized.Finalization!.FinalizedBy);
        Assert.Equal(EvaluationDraftDatabaseFixture.Now, finalized.Finalization.FinalizedAt);
        Assert.Equal(1, finalized.Finalization.Evidence.ArtifactCount);
        Assert.Equal(1, finalized.Finalization.Evidence.FileCount);
        Assert.Single(finalized.Finalization.Evidence.DeliverableVersionIds);
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(lecturer, finalized)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Save(lecturer, finalized, s)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke",
            new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Cannot discard finalized grade"))).StatusCode);
        await using (var db = database.CreateContext())
        {
            var row = (await db.Evaluations.FindAsync(draft.Id))!;
            Assert.Equal(8.6m, row.TotalScore);
            Assert.Equal(EvaluationDraftDatabaseFixture.Now, row.EvaluatedAt);
            Assert.Equal(detailIds, await db.EvaluationDetails.Where(d => d.EvaluationId == draft.Id).OrderBy(d => d.Id).Select(d => d.Id).ToArrayAsync());
            var notification = await db.Notifications.Include(n => n.NotificationRecipients).SingleAsync(n => n.RelatedEntityType == "EVALUATION" && n.RelatedEntityId == draft.Id);
            Assert.Equal(s.Scope.Users.Staff, Assert.Single(notification.NotificationRecipients).UserId);
            Assert.Single(await db.AuditLogs.Where(a => a.Action == "EVALUATION_FINALIZED" && a.EntityId == draft.Id.ToString()).ToListAsync());
            row.Status = "DRAFT";
            row.TotalScore = 0;
            (await db.Rubrics.FindAsync(s.RubricId))!.Name = "Changed live rubric";
            await db.SaveChangesAsync();
        }
        var read = await Body<EvaluationDraftDto>(await staff.GetAsync(DraftUrl(draft.Id)));
        Assert.Equal("FINALIZED", read.Status);
        Assert.Equal("Evaluation rubric", read.RubricName);
        Assert.Equal(8.6m, read.TotalScore);
        Assert.Equal(HttpStatusCode.Conflict, (await Save(lecturer, read, s)).StatusCode);
        await database.Migrate();
        Assert.Equal(finalized.ConcurrencyToken, (await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)))).ConcurrencyToken);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("admin")]
    [InlineData("student")]
    [InlineData("peer")]
    [InlineData("outside")]
    public async Task Nonassigned_roles_cannot_finalize(string role)
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        var id = role switch { "staff" => s.Scope.Users.Staff, "admin" => s.Scope.Users.Admin,
            "student" => s.Scope.Users.Student, "peer" => s.Scope.Users.NewLecturer, _ => s.Scope.Users.OtherLecturer };
        using var client = app.CreateAuthenticatedClient(id, roles: ["LECTURER", "ADMIN"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await Finalize(client, draft)).StatusCode);
        await NotFinalized(draft.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Any_missing_weighted_criterion_blocks_finalize(bool required)
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var keep = draft.Scores.Single(c => c.IsRequired != required);
        var partial = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, null, [new(keep.RubricCriterionId, keep.Score, null)])));
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(lecturer, partial)).StatusCode);
        await NotFinalized(draft.Id);
    }

    [Theory]
    [InlineData("closedWindow")]
    [InlineData("overlap")]
    [InlineData("project")]
    [InlineData("revoked")]
    [InlineData("role")]
    [InlineData("inactive")]
    [InlineData("rubric")]
    [InlineData("score")]
    [InlineData("missingPackage")]
    public async Task Finalize_revalidates_eligibility_and_scoring(string change)
    {
        using var app = new EvaluationFactory(database);
        var (s, assignment, draft) = await Prepared(app);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        await using (var db = database.CreateContext())
        {
            switch (change)
            {
                case "closedWindow": (await db.ProjectPeriods.FindAsync(s.PeriodId))!.EndAt = EvaluationDraftDatabaseFixture.Now; break;
                case "overlap": db.ProjectPeriods.Add(new() { AcademicSemesterId = s.Scope.SemesterId, Code = Guid.NewGuid().ToString("N"), Name = "Overlap", PeriodType = "EVALUATION", Status = "ACTIVE", StartAt = EvaluationDraftDatabaseFixture.Now.AddHours(-1), EndAt = EvaluationDraftDatabaseFixture.Now.AddHours(1) }); break;
                case "project": (await db.Projects.FindAsync(s.ProjectId))!.Status = "COMPLETED"; break;
                case "revoked": var a = (await db.Set<EvaluationAssignment>().FindAsync(assignment.Id))!; a.Status = "REVOKED"; a.RevokedAt = EvaluationDraftDatabaseFixture.Now; a.RevocationReason = "Changed"; break;
                case "role": db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Scope.Users.Lecturer).ToListAsync()); break;
                case "inactive": (await db.Users.FindAsync(s.Scope.Users.Lecturer))!.Status = "INACTIVE"; break;
                case "rubric": (await db.Set<RubricVersion>().FindAsync(s.RubricId))!.Status = "DRAFT"; break;
                case "score": (await db.EvaluationDetails.FirstAsync(d => d.EvaluationId == draft.Id)).Score = 999m; break;
                case "missingPackage": var package = await db.Set<FinalSubmission>().Include(f => f.Items).SingleAsync(f => f.ProjectId == s.ProjectId); db.Set<FinalSubmissionItem>().RemoveRange(package.Items); break;
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(change is "revoked" or "role" or "inactive" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict,
            (await Finalize(lecturer, draft)).StatusCode);
        await NotFinalized(draft.Id);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("finalize")]
    [InlineData("revoke")]
    public async Task Concurrent_finalize_and_mutation_preserve_final_scores(string operation)
    {
        using var app = new EvaluationFactory(database);
        var (s, assignment, draft) = await Prepared(app);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        var final = Finalize(lecturer, draft);
        var mutation = operation switch { "save" => Save(lecturer, draft, s), "finalize" => Finalize(lecturer, draft),
            _ => staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke", new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Changed")) };
        var responses = await Task.WhenAll(final, mutation);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Forbidden }));
        Assert.True(responses.Count(r => r.IsSuccessStatusCode) <= 1);
        if (responses.All(r => !r.IsSuccessStatusCode)) await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        await using var db = database.CreateContext();
        var snapshots = await db.Set<EvaluationFinalization>().Where(f => f.EvaluationId == draft.Id).ToListAsync();
        Assert.True(snapshots.Count <= 1);
        if (snapshots.Count == 1)
        {
            Assert.Equal("FINALIZED", (await db.Evaluations.FindAsync(draft.Id))!.Status);
            Assert.Equal(8.6m, (await db.Evaluations.FindAsync(draft.Id))!.TotalScore);
            Assert.Single(await db.Notifications.Where(n => n.NotificationType == "EVALUATION_FINALIZED" && n.RelatedEntityId == draft.Id).ToListAsync());
        }
        else await NotFinalized(draft.Id);
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("notification")]
    [InlineData("deadline")]
    public async Task Late_failure_rolls_back_finalization(string failure)
    {
        using var setup = new EvaluationFactory(database);
        var (_, _, draft) = await Prepared(setup);
        using var app = new FinalizationFailureFactory(database, failure);
        using var client = app.CreateAuthenticatedClient(draft.EvaluatorId);
        Assert.Equal(failure == "deadline" ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError, (await Finalize(client, draft)).StatusCode);
        await NotFinalized(draft.Id);
    }

    private async Task NotFinalized(long id)
    {
        await using var db = database.CreateContext();
        var row = await db.Evaluations.FindAsync(id);
        Assert.Equal("DRAFT", row!.Status);
        Assert.Null(row.EvaluatedAt);
        Assert.False(await db.Set<EvaluationFinalization>().AnyAsync(f => f.EvaluationId == id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "EVALUATION_FINALIZED" && a.EntityId == id.ToString()));
        Assert.False(await db.Notifications.AnyAsync(n => n.NotificationType == "EVALUATION_FINALIZED" && n.RelatedEntityId == id));
    }

    [Fact]
    public async Task Stale_confirmation_and_bad_input_cannot_finalize_new_scores()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var anonymous = app.CreateClient();
        var saved = await Body<EvaluationDraftDto>(await Save(lecturer, draft, s));
        Assert.Equal(HttpStatusCode.Conflict, (await Finalize(lecturer, draft)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Finalize(anonymous, saved)).StatusCode);
        var invalid = await lecturer.PostAsJsonAsync(DraftUrl(draft.Id) + "/finalize", new FinalizeEvaluationRequest(""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        await Body<EvaluationDraftDto>(await Finalize(lecturer, saved));
    }

    [Fact]
    public async Task Each_evaluator_finalizes_independently_without_publishing_or_completing_project()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var first = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var second = app.CreateAuthenticatedClient(s.Scope.Users.NewLecturer);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        var assignment = await Assign(staff, s, s.Scope.Users.NewLecturer);
        var other = await Create(second, assignment.Id);
        other = await Body<EvaluationDraftDto>(await Save(second, other, s));
        await Body<EvaluationDraftDto>(await Finalize(first, draft));
        Assert.Equal("DRAFT", (await Body<EvaluationDraftDto>(await second.GetAsync(DraftUrl(other.Id)))).Status);
        await Body<EvaluationDraftDto>(await Finalize(second, other));
        Assert.Equal(HttpStatusCode.Forbidden, (await first.GetAsync(DraftUrl(other.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(DraftUrl(draft.Id))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("FINAL_SUBMISSION", (await db.Projects.FindAsync(s.ProjectId))!.Status);
        Assert.Equal(2, await db.Set<EvaluationFinalization>().CountAsync(f => f.EvaluationId == draft.Id || f.EvaluationId == other.Id));
    }

    [Fact]
    public async Task Assigned_retired_rubric_can_finalize_but_ended_supervision_cannot()
    {
        using var app = new EvaluationFactory(database);
        var (s, _, draft) = await Prepared(app);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var supervisor = await Assign(staff, s, type: "SUPERVISOR");
        var supervisorDraft = await Create(lecturer, supervisor.Id);
        supervisorDraft = await Body<EvaluationDraftDto>(await Save(lecturer, supervisorDraft, s));
        await using (var db = database.CreateContext())
        {
            (await db.Set<RubricVersion>().FindAsync(s.RubricId))!.Status = "RETIRED";
            (await db.Rubrics.FindAsync(s.RubricId))!.IsActive = false;
            (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == s.ProjectId)).EndedAt = EvaluationDraftDatabaseFixture.Now;
            await db.SaveChangesAsync();
        }
        await Body<EvaluationDraftDto>(await Finalize(lecturer, draft));
        Assert.Equal(HttpStatusCode.Forbidden, (await Finalize(lecturer, supervisorDraft)).StatusCode);
        await NotFinalized(supervisorDraft.Id);
    }
}

internal sealed class FinalizationFailureFactory(EvaluationDraftDatabaseFixture database, string failure) : EvaluationFactory(database)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var clock = new FinalizationClock();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            var original = services.Single(s => s.ServiceType == typeof(IWorkflowNotificationWriter));
            services.Remove(original);
            services.AddScoped<IWorkflowNotificationWriter>(p => new FinalNotificationProbe(
                (IWorkflowNotificationWriter)ActivatorUtilities.CreateInstance(p, original.ImplementationType!), failure, clock));
            if (failure == "audit")
            {
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail, FinalAuditFailure>();
            }
        });
    }
    private sealed class FinalizationClock : TimeProvider
    {
        public DateTime Now { get; set; } = EvaluationDraftDatabaseFixture.Now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private sealed class FinalNotificationProbe(IWorkflowNotificationWriter inner, string failure, FinalizationClock clock) : IWorkflowNotificationWriter
    {
        public async Task WriteAsync(WorkflowNotificationEvent notification, CancellationToken ct)
        {
            await inner.WriteAsync(notification, ct);
            if (failure == "notification") throw new InvalidOperationException("Injected notification failure");
            if (failure == "deadline") clock.Now = clock.Now.AddDays(1);
        }
    }
    private sealed class FinalAuditFailure : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure");
    }
}
