using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.UnitTests.Application;

public sealed class EvaluationHandlerValidatorTests
{
    [Fact]
    public async Task Rubric_cannot_activate_when_weights_are_not_100()
    {
        var repository = new StubRepository { Rubric = Rubric("DRAFT", false, 90m) };
        var handler = new SetRubricActiveHandler(new User(1, "DEPARTMENT_STAFF"), repository, TimeProvider.System);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(new SetRubricActiveCommand(1, true), default));
        Assert.False(repository.SetActiveCalled);
    }

    [Fact]
    public async Task Approved_rubric_version_is_immutable()
    {
        var repository = new StubRepository { Rubric = Rubric("APPROVED", true, 100m) };
        var handler = new UpsertRubricCriterionHandler(new User(1, "ADMIN"), repository);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpsertRubricCriterionCommand(1, 2, 100, 10, 0, true), default));
    }

    [Fact]
    public async Task Score_above_rubric_max_is_rejected()
    {
        var repository = new StubRepository { Evaluation = Evaluation("DRAFT"), CanEvaluate = true };
        var handler = new UpdateEvaluationScoreHandler(new User(5), repository);
        await Assert.ThrowsAsync<AIPMS.Application.Common.Exceptions.ValidationException>(() => handler.Handle(
            new UpdateEvaluationScoreCommand(7, 11, 10.01m, null), default));
        Assert.False(repository.ScoreUpdated);
    }

    [Fact]
    public async Task Non_assigned_evaluator_is_rejected()
    {
        var repository = new StubRepository { Evaluation = Evaluation("DRAFT"), CanEvaluate = false };
        var handler = new UpdateEvaluationScoreHandler(new User(5), repository);
        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new UpdateEvaluationScoreCommand(7, 11, 8m, null), default));
    }

    [Fact]
    public async Task Finalized_evaluation_is_immutable()
    {
        var repository = new StubRepository { Evaluation = Evaluation("FINALIZED"), CanEvaluate = true };
        var handler = new UpdateEvaluationCommentHandler(new User(5), repository);
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new UpdateEvaluationCommentCommand(7, "change", "change"), default));
    }

    [Fact]
    public async Task Validators_reject_invalid_rubric_and_score_inputs()
    {
        Assert.False((await new CreateRubricValidator().ValidateAsync(
            new CreateRubricCommand(null, null, "", "", null, 0))).IsValid);
        Assert.False((await new UpdateEvaluationScoreValidator().ValidateAsync(
            new UpdateEvaluationScoreCommand(0, 0, -1, null))).IsValid);
    }

    private static RubricDto Rubric(string status, bool active, decimal weight) => new(
        1, null, 1, "R-V1", "Rubric", null, 1, status, active, 1,
        DateTime.UtcNow, DateTime.UtcNow, active ? 1 : null, active ? DateTime.UtcNow : null,
        [new RubricCriterionDto(11, 2, "C1", "Criterion", null, weight, 10, 0, true)]);

    private static EvaluationDetailDto Evaluation(string status) => new(
        new EvaluationDto(7, 3, 5, 1, 1, "SUPERVISOR", status, null, null,
            "Evidence", null, null, null, DateTime.UtcNow, DateTime.UtcNow),
        [new EvaluationScoreDto(11, 2, "C1", "Criterion", 100, 10, true, 0, null, null)]);

    private sealed class User(long id, params string[] roles) : ICurrentUser
    { public bool IsAuthenticated => true; public long? UserId => id; public string? Email => null; public string? FullName => null; public IReadOnlyCollection<string> Roles => roles; }

    private sealed class StubRepository : IEvaluationRepository
    {
        public RubricDto? Rubric { get; init; }
        public EvaluationDetailDto? Evaluation { get; init; }
        public bool CanEvaluate { get; init; }
        public bool SetActiveCalled { get; private set; }
        public bool ScoreUpdated { get; private set; }
        public Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> CanEvaluateProjectAsync(long userId, long projectId, CancellationToken ct) => Task.FromResult(CanEvaluate);
        public Task<RubricDto?> GetRubricAsync(long id, CancellationToken ct) => Task.FromResult(Rubric);
        public Task<PagedResult<RubricDto>> ListRubricsAsync(bool? active, int page, int size, CancellationToken ct) => Task.FromResult(new PagedResult<RubricDto>([], 1, 20, 0));
        public Task<long> CreateRubricAsync(CreateRubricData data, CancellationToken ct) => Task.FromResult(1L);
        public Task<bool> UpdateRubricAsync(long id, UpdateRubricData data, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> DeleteRubricAsync(long id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> UpsertRubricCriterionAsync(long rubricId, UpsertRubricCriterionData data, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> DeleteRubricCriterionAsync(long rubricId, long rubricCriterionId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ReorderRubricCriteriaAsync(long rubricId, IReadOnlyList<long> orderedIds, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetRubricActiveAsync(long rubricId, bool active, long actorId, DateTime at, CancellationToken ct) { SetActiveCalled = true; return Task.FromResult(true); }
        public Task<long> CreateDraftAsync(long projectId, long evaluatorId, long rubricId, string evaluationType, string? evidenceSummary, CancellationToken ct) => Task.FromResult(7L);
        public Task<EvaluationDetailDto?> GetAsync(long id, CancellationToken ct) => Task.FromResult(Evaluation);
        public Task<PagedResult<EvaluationDto>> GetByProjectAsync(long projectId, int page, int size, CancellationToken ct) => Task.FromResult(new PagedResult<EvaluationDto>([], 1, 20, 0));
        public Task<bool> UpsertScoreAsync(long evaluationId, long rubricCriterionId, decimal score, string? comments, CancellationToken ct) { ScoreUpdated = true; return Task.FromResult(true); }
        public Task<bool> UpdateCommentsAsync(long id, string? comments, string? evidenceSummary, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> FinalizeAsync(long id, decimal total, long actorId, DateTime at, CancellationToken ct) => Task.FromResult(true);
    }
}

