using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Results.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class EvaluationDraftEndpointTests
{
    [Fact]
    public async Task Hierarchical_rubric_scores_only_leaves_and_preserves_finalized_result()
    {
        var s = await database.Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var rubric = await Body<RubricDto>(await staff.PostAsJsonAsync("/api/v1/rubrics",
            new CreateRubricRequest(s.Scope.Users.DepartmentId, s.Scope.SemesterId,
                "TREE_" + Guid.NewGuid().ToString("N"), "Tree", null, HierarchicalRubricSample.Tree())));
        rubric = await Body<RubricDto>(await staff.PostAsJsonAsync($"/api/v1/rubrics/{rubric.Id}/publish",
            new ChangeRubricStatusRequest(rubric.ConcurrencyToken)));
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(s.PeriodId))!.RubricId = rubric.Id;
            await db.SaveChangesAsync();
        }
        var assignment = await Assign(staff, s);
        var draft = await Create(lecturer, assignment.Id);
        Assert.Equal(4, draft.Scores.Count);
        Assert.Equal(new[] { "Clarity", "Scope", "Requirements Quality", "Implementation" }, draft.Scores.Select(c => c.Name));
        Assert.Equal(new[] { 4.8m, 3.2m, 12m, 80m }, draft.Scores.Select(c => c.WeightPercent));
        var leaves = HierarchicalRubricSample.Flatten(rubric.Criteria).Where(c => c.Children.Count == 0).ToArray();
        var invalid = await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft",
            new SaveEvaluationDraftRequest(draft.ConcurrencyToken, null, [new(rubric.Criteria[0].Id, 8, null)]));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Equal(draft.ConcurrencyToken, (await Body<EvaluationDraftDto>(await lecturer.GetAsync(DraftUrl(draft.Id)))).ConcurrencyToken);
        using var failingApp = new EvaluationFactory(database, failAudit: true);
        using var failing = failingApp.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        EvaluationScoreInput[] scores = leaves.Select(c => new EvaluationScoreInput(c.Id,
            c.Name == "Clarity" ? 5m : c.MaxScore, "Evidence")).ToArray();
        var request = new SaveEvaluationDraftRequest(draft.ConcurrencyToken, "Tree score", scores);
        Assert.Equal(HttpStatusCode.InternalServerError, (await failing.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft", request)).StatusCode);
        var saved = await Body<EvaluationDraftDto>(await lecturer.PutAsJsonAsync(DraftUrl(draft.Id) + "/draft", request));
        Assert.Equal(9.76m, saved.TotalScore);
        await Body<ResultPolicyDto>(await staff.PutAsJsonAsync($"/api/v1/projects/{s.ProjectId}/result-policy",
            new ConfigureResultPolicyRequest(5m, [new(assignment.Id, 100m)], null)));
        var finalized = await Body<EvaluationDraftDto>(await Finalize(lecturer, saved));
        Assert.Equal(9.76m, finalized.TotalScore);
        Assert.Equal("FINALIZED", finalized.Status);
        var preview = await ResultPreview(staff, s.ProjectId);
        Assert.True(preview.CanPublish);
        Assert.Equal(9.76m, preview.TotalScore);
        var result = await Body<ProjectResultDto>(await PublishResult(staff, preview));
        Assert.Equal(9.76m, result.TotalScore);
        await using (var db = database.CreateContext())
        {
            var persisted = await db.EvaluationDetails.Where(d => d.EvaluationId == draft.Id).ToListAsync();
            Assert.Equal(4, persisted.Count);
            Assert.All(persisted, d => Assert.Contains(leaves, c => c.Id == d.RubricCriterionId));
            Assert.Single(await db.AuditLogs.Where(a => a.Action == "EVALUATION_DRAFT_SAVED" && a.EntityId == draft.Id.ToString()).ToListAsync());
        }
        await database.Migrate();
        var after = await Body<EvaluationDraftDto>(await staff.GetAsync(DraftUrl(draft.Id)));
        Assert.Equal(JsonSerializer.Serialize(finalized), JsonSerializer.Serialize(after));
    }
}
