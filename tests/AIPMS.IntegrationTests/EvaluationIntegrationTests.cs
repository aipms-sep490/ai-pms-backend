using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests;

public sealed class EvaluationIntegrationTests : IClassFixture<AipmsWebApplicationFactory>
{
    private readonly AipmsWebApplicationFactory _factory;
    public EvaluationIntegrationTests(AipmsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async System.Threading.Tasks.Task Sample_supervisor_can_create_score_and_finalize_evaluation()
    {
        long projectId;
        long supervisorUserId;
        List<(long Id, decimal MaxScore)> criteria;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AipmsDbContext>();
            var assignment = await db.SupervisorAssignments.AsNoTracking().Include(x => x.SupervisorProfile).FirstAsync(x => x.EndedAt == null);
            projectId = assignment.ProjectId;
            supervisorUserId = assignment.SupervisorProfile.UserId;
            criteria = await db.EvaluationCriteria.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Id).Select(x => new ValueTuple<long, decimal>(x.Id, x.MaxScore)).ToListAsync();
        }

        Assert.NotEmpty(criteria);
        var client = _factory.CreateAuthenticatedClient(supervisorUserId, roles: "LECTURER");
        var create = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/evaluations", new { evaluationType = "SUPERVISOR" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var draft = await create.Content.ReadFromJsonAsync<EvaluationDetailDto>();
        Assert.NotNull(draft);
        Assert.Equal("DRAFT", draft.Evaluation.Status);
        Assert.Equal(criteria.Count, draft.Scores.Count);

        foreach (var criterion in criteria)
        {
            var score = await client.PutAsJsonAsync($"/api/v1/evaluations/{draft.Evaluation.Id}/criteria/{criterion.Id}/score", new { score = criterion.MaxScore * 0.85m, comments = "BE-09 sample-data integration test" });
            Assert.Equal(HttpStatusCode.NoContent, score.StatusCode);
        }

        var finalize = await client.PostAsync($"/api/v1/evaluations/{draft.Evaluation.Id}/finalize", null);
        Assert.Equal(HttpStatusCode.NoContent, finalize.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AipmsDbContext>();
        var saved = await verifyDb.Evaluations.AsNoTracking().SingleAsync(x => x.Id == draft.Evaluation.Id);
        Assert.Equal("FINALIZED", saved.Status);
        Assert.Equal(8.50m, saved.TotalScore);
        Assert.NotNull(saved.EvaluatedAt);
        Assert.Equal(criteria.Count, await verifyDb.EvaluationDetails.CountAsync(x => x.EvaluationId == saved.Id));
    }
}
