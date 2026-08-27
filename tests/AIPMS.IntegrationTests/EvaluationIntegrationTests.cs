using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests;

[Collection("Evaluation database")]
public sealed class EvaluationIntegrationTests(AipmsWebApplicationFactory factory)
    : IClassFixture<AipmsWebApplicationFactory>
{
    [Fact]
    public async System.Threading.Tasks.Task Rubric_draft_score_finalize_and_concurrency_flow()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        var project = await db.Projects.AsNoTracking().Select(x => new
        {
            x.Id,
            SemesterId = x.Team.AcademicSemesterId,
            LeaderId = x.Team.TeamMembers.Where(m => m.IsLeader && m.LeftAt == null).Select(m => m.UserId).First()
        }).FirstAsync();
        var supervisor = await db.SupervisorProfiles.AsNoTracking().FirstAsync();
        long rubricId = 0, evaluationId = 0, requestId = 0, assignmentId = 0;
        var criterionIds = new List<long>();
        try
        {
            var marker = DateTime.UtcNow.Ticks;
            var criteria = new[]
            {
                new EvaluationCriterion { Code = $"BE09A{marker}", Name = "Architecture", IsActive = true },
                new EvaluationCriterion { Code = $"BE09Q{marker}", Name = "Quality", IsActive = true }
            };
            db.EvaluationCriteria.AddRange(criteria); await db.SaveChangesAsync();
            criterionIds.AddRange(criteria.Select(x => x.Id));

            var now = DateTime.UtcNow;
            var request = new SupervisorRequest { ProjectId = project.Id, SupervisorProfileId = supervisor.Id,
                RequestedBy = project.LeaderId, Status = "ACCEPTED", RequestedAt = now,
                RespondedAt = now, CreatedAt = now, UpdatedAt = now };
            db.SupervisorRequests.Add(request); await db.SaveChangesAsync(); requestId = request.Id;
            var assignment = new SupervisorAssignment { ProjectId = project.Id, SupervisorProfileId = supervisor.Id,
                SupervisorRequestId = request.Id, IsPrimary = true, AssignedAt = now, CreatedAt = now, UpdatedAt = now };
            db.SupervisorAssignments.Add(assignment); await db.SaveChangesAsync(); assignmentId = assignment.Id;

            var staff = factory.CreateAuthenticatedClient(project.LeaderId, roles: "DEPARTMENT_STAFF");
            var createRubric = await staff.PostAsJsonAsync("/api/v1/rubrics",
                new CreateRubricPayload(null, project.SemesterId, $"BE09-{marker}", "BE-09 rubric", null, 1));
            Assert.Equal(HttpStatusCode.OK, createRubric.StatusCode);
            var rubric = await createRubric.Content.ReadFromJsonAsync<RubricDto>(); rubricId = rubric!.Id;

            Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}/criteria/{criteria[0].Id}",
                new UpsertRubricCriterionPayload(60m, 10m, 0, true))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}/active",
                new SetRubricActivePayload(true))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}/criteria/{criteria[1].Id}",
                new UpsertRubricCriterionPayload(40m, 20m, 1, true))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}/active",
                new SetRubricActivePayload(true))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rubrics/{rubricId}/criteria/{criteria[0].Id}",
                new UpsertRubricCriterionPayload(50m, 10m, 0, true))).StatusCode);

            rubric = await (await staff.GetAsync($"/api/v1/rubrics/{rubricId}")).Content.ReadFromJsonAsync<RubricDto>();
            Assert.Equal("APPROVED", rubric!.ApprovalStatus);
            var evaluator = factory.CreateAuthenticatedClient(supervisor.UserId, roles: "LECTURER");
            var createEvaluation = await evaluator.PostAsJsonAsync($"/api/v1/projects/{project.Id}/evaluations",
                new CreateEvaluationPayload(rubricId, "SUPERVISOR", "Reviewed repository, demo and test evidence."));
            Assert.Equal(HttpStatusCode.OK, createEvaluation.StatusCode);
            var draft = await createEvaluation.Content.ReadFromJsonAsync<EvaluationDetailDto>(); evaluationId = draft!.Evaluation.Id;
            Assert.Equal(rubricId, draft.Evaluation.RubricId);

            var outsiderId = await db.Users.Where(x => x.Id != supervisor.UserId && x.Id != project.LeaderId).Select(x => x.Id).FirstAsync();
            var outsider = factory.CreateAuthenticatedClient(outsiderId, roles: "LECTURER");
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync($"/api/v1/projects/{project.Id}/evaluations",
                new CreateEvaluationPayload(rubricId, "SUPERVISOR", "Invalid"))).StatusCode);

            Assert.Equal(HttpStatusCode.BadRequest, (await evaluator.PutAsJsonAsync(
                $"/api/v1/evaluations/{evaluationId}/criteria/{draft.Scores[0].RubricCriterionId}/score",
                new ScorePayload(11m, null))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await evaluator.PutAsJsonAsync(
                $"/api/v1/evaluations/{evaluationId}/criteria/{draft.Scores[0].RubricCriterionId}/score",
                new ScorePayload(8m, "Architecture evidence"))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await evaluator.PutAsJsonAsync(
                $"/api/v1/evaluations/{evaluationId}/criteria/{draft.Scores[1].RubricCriterionId}/score",
                new ScorePayload(18m, "Quality evidence"))).StatusCode);

            var finalizeCalls = await System.Threading.Tasks.Task.WhenAll(
                evaluator.PostAsync($"/api/v1/evaluations/{evaluationId}/finalize", null),
                evaluator.PostAsync($"/api/v1/evaluations/{evaluationId}/finalize", null));
            Assert.Single(finalizeCalls, x => x.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(finalizeCalls, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(HttpStatusCode.Conflict, (await evaluator.PutAsJsonAsync(
                $"/api/v1/evaluations/{evaluationId}/criteria/{draft.Scores[0].RubricCriterionId}/score",
                new ScorePayload(9m, null))).StatusCode);

            var saved = await db.Evaluations.AsNoTracking().SingleAsync(x => x.Id == evaluationId);
            Assert.Equal("FINALIZED", saved.Status); Assert.Equal(8.40m, saved.TotalScore);
            var audit = await db.Set<EvaluationAudit>().AsNoTracking().SingleAsync(x => x.EvaluationId == evaluationId);
            Assert.Equal(supervisor.UserId, audit.FinalizedBy); Assert.NotNull(audit.FinalizedAt);
            Assert.Equal("2_DECIMALS_AWAY_FROM_ZERO", audit.RoundingRule);
        }
        finally
        {
            if (evaluationId != 0) { await db.Evaluations.Where(x => x.Id == evaluationId).ExecuteDeleteAsync(); }
            if (assignmentId != 0) await db.SupervisorAssignments.Where(x => x.Id == assignmentId).ExecuteDeleteAsync();
            if (requestId != 0) await db.SupervisorRequests.Where(x => x.Id == requestId).ExecuteDeleteAsync();
            if (rubricId != 0) await db.Rubrics.Where(x => x.Id == rubricId).ExecuteDeleteAsync();
            if (criterionIds.Count > 0) await db.EvaluationCriteria.Where(x => criterionIds.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
}

[CollectionDefinition("Evaluation database", DisableParallelization = true)]
public sealed class EvaluationDatabaseCollection;

