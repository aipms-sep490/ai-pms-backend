using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Correction_foundation_preserves_v1_recalculates_affected_targets_and_redacts_history()
    {
        var (s, major) = await Seed();
        var evidence = new ApiAcceptanceEvidence("expansion-result-revision-foundation");
        using var app = new EvaluationFactory(database) { AcceptanceEvidence = evidence };
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(s.Scope.Users.Student);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        using var outsider = app.CreateAuthenticatedClient(s.Scope.Users.OtherLecturer);
        var scheme = await Scheme(staff, s, major);
        var common = await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "COMMON"), 8);
        var individual = await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "INDIVIDUAL"), 6);
        var projectUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var studentUrl = $"/api/v1/projects/{s.ProjectId}/students/{s.Scope.Users.Student}/result";
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(studentUrl + "/preview"));
        await Body<StudentResultDto>(await staff.PostAsJsonAsync(studentUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
        preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(projectUrl + "/preview"));
        await Body<ProjectResultDto>(await staff.PostAsJsonAsync(projectUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
        async Task<(long Id, string Token)> Draft(long evaluation, decimal score)
        {
            using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var store = scope.ServiceProvider.GetRequiredService<ResultRevisionStore>();
            var bundle = await store.Create(s.ProjectId, s.Scope.Users.NewLecturer, "Synthetic correction",
                [new(evaluation, s.Criteria[0], score, "Correct transcription")], default);
            var check = await store.Check(bundle, default); await tx.CommitAsync(); return (bundle.Id, check.ConfirmationToken);
        }
        async Task Publish((long Id, string Token) input, long actor, bool failAudit = false)
        {
            using var failed = failAudit ? new EvaluationFactory(database, failAudit: true) : null;
            using var scope = (failed ?? app).Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var bundle = await db.Set<ResultCorrectionBundle>().Include(b => b.Amendments).SingleAsync(b => b.Id == input.Id);
            await scope.ServiceProvider.GetRequiredService<ResultRevisionStore>().Publish(bundle, actor, input.Token, default);
            await tx.CommitAsync();
        }
        var individualEdit = await Draft(individual.Id, 10);
        var competing = await Draft(individual.Id, 9);
        await Assert.ThrowsAsync<ForbiddenException>(() => Publish(individualEdit, s.Scope.Users.NewLecturer));
        await Assert.ThrowsAsync<ForbiddenException>(() => Publish(individualEdit, s.Scope.Users.Lecturer));
        await Assert.ThrowsAsync<ConflictException>(() => Publish((individualEdit.Id, "stale"), s.Scope.Users.Staff));
        var auditFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => Publish(individualEdit, s.Scope.Users.Staff, failAudit: true));
        Assert.Equal("Injected evaluation audit failure", auditFailure.Message);
        await using (var db = database.CreateContext()) Assert.False(await db.Set<ResultRevision>().AnyAsync(r => r.ProjectId == s.ProjectId));
        await Publish(individualEdit, s.Scope.Users.Staff);
        await Assert.ThrowsAsync<ConflictException>(() => Publish(competing, s.Scope.Users.Staff));
        var revised = await Body<StudentResultDto>(await student.GetAsync(studentUrl));
        Assert.Equal(8.12m, revised.TotalScore); Assert.Equal(2, revised.Version); Assert.NotNull(revised.RevisionId);
        Assert.Equal("{}", revised.SnapshotJson);
        var unchangedProject = await Body<ProjectResultDto>(await staff.GetAsync(projectUrl));
        Assert.Equal(8m, unchangedProject.TotalScore); Assert.Equal(1, unchangedProject.Version);
        var commonEdit = await Draft(common.Id, 10);
        await Publish(commonEdit, s.Scope.Users.Staff);
        revised = await Body<StudentResultDto>(await student.GetAsync(studentUrl));
        Assert.Equal(8.96m, revised.TotalScore); Assert.Equal(3, revised.Version);
        var revisedProject = await Body<ProjectResultDto>(await staff.GetAsync(projectUrl));
        Assert.Equal(9.2m, revisedProject.TotalScore); Assert.Equal(2, revisedProject.Version);
        var old = await Body<StudentResultDto>(await student.GetAsync(studentUrl + "/versions/1"));
        Assert.Equal(7.4m, old.TotalScore); Assert.Equal("{}", old.SnapshotJson);
        var history = await Body<PagedResult<ResultRevisionSummary>>(await student.GetAsync(studentUrl + "/history?pageSize=2"));
        Assert.Equal(3, history.TotalCount); Assert.Equal(new[] { 3, 2 }, history.Items.Select(i => i.Version));
        Assert.All(history.Items, i => Assert.Null(i.Reason));
        var last = await Body<PagedResult<ResultRevisionSummary>>(await student.GetAsync(studentUrl + "/history?pageSize=2&page=2"));
        Assert.Equal(1, Assert.Single(last.Items).Version);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(studentUrl + "/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(studentUrl + "/versions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync(studentUrl + "/history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync(studentUrl + "/versions/999")).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.Equal(7.4m, (await db.Set<StudentResult>().SingleAsync(r => r.ProjectId == s.ProjectId)).TotalScore);
            Assert.Equal(8m, PublishedResultResolver.Root(await db.Set<ProjectResult>().SingleAsync(r => r.ProjectId == s.ProjectId)).TotalScore);
            Assert.Equal(6m, (await db.Evaluations.FindAsync(individual.Id))!.TotalScore);
            Assert.Equal("COMPLETED", (await db.Projects.FindAsync(s.ProjectId))!.Status);
            Assert.Equal(3, await db.Set<ResultRevision>().CountAsync(r => r.ProjectId == s.ProjectId));
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "RESULT_CORRECTION_PUBLISHED"
                && db.Set<ResultCorrectionBundle>().Any(b => b.ProjectId == s.ProjectId && b.Id.ToString() == a.EntityId)));
        }
        var concurrentA = await Draft(common.Id, 9);
        var concurrentB = await Draft(common.Id, 7);
        async Task<bool> Attempt((long Id, string Token) draft)
        { try { await Publish(draft, s.Scope.Users.Staff); return true; } catch (ConflictException) { return false; } }
        var attempts = await Task.WhenAll(Attempt(concurrentA), Attempt(concurrentB));
        Assert.Single(attempts.Where(ok => ok));
        await evidence.SaveAsync(new { publicationViaApi = true, correctionViaInternalStore = true,
            originalProjectScore = 8m, originalStudentScore = 7.4m, individualRevision = 8.12m,
            commonProjectRevision = 9.2m, commonStudentRevision = 8.96m,
            auditRollbackVerified = true, concurrentPublishWinners = 1, historyRedacted = true,
            limitation = "Correction API authorization and policy workflow are delivered in the next slice, not exercised here." });
    }
}
