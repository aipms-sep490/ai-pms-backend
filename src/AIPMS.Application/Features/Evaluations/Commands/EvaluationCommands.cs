using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Commands;

public sealed record CreateEvaluationDraftCommand(long ProjectId, long RubricId,
    string EvaluationType, string? EvidenceSummary) : IRequest<EvaluationDetailDto>;
public sealed record UpdateEvaluationScoreCommand(long EvaluationId, long RubricCriterionId,
    decimal Score, string? Comments) : IRequest<Unit>;
public sealed record UpdateEvaluationCommentCommand(long EvaluationId, string? Comments,
    string? EvidenceSummary) : IRequest<Unit>;
public sealed record FinalizeEvaluationCommand(long EvaluationId) : IRequest<Unit>;

public sealed class CreateEvaluationDraftValidator : AbstractValidator<CreateEvaluationDraftCommand>
{
    public CreateEvaluationDraftValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.RubricId).GreaterThan(0);
        RuleFor(x => x.EvaluationType).NotEmpty().Must(x =>
            new[] { "SUPERVISOR", "LECTURER", "COMMITTEE", "FINAL" }
                .Contains(x.ToUpperInvariant()));
        RuleFor(x => x.EvidenceSummary).MaximumLength(4000);
    }
}
public sealed class UpdateEvaluationScoreValidator : AbstractValidator<UpdateEvaluationScoreCommand>
{ public UpdateEvaluationScoreValidator() { RuleFor(x => x.EvaluationId).GreaterThan(0); RuleFor(x => x.RubricCriterionId).GreaterThan(0); RuleFor(x => x.Score).GreaterThanOrEqualTo(0); RuleFor(x => x.Comments).MaximumLength(2000); } }
public sealed class UpdateEvaluationCommentValidator : AbstractValidator<UpdateEvaluationCommentCommand>
{ public UpdateEvaluationCommentValidator() { RuleFor(x => x.EvaluationId).GreaterThan(0); RuleFor(x => x.EvidenceSummary).MaximumLength(4000); } }
public sealed class FinalizeEvaluationValidator : AbstractValidator<FinalizeEvaluationCommand>
{ public FinalizeEvaluationValidator() { RuleFor(x => x.EvaluationId).GreaterThan(0); } }

public static class EvaluationAccess
{
    public static long UserId(ICurrentUser user) => user.UserId ?? throw new ForbiddenException();
    public static async Task Ensure(long userId, EvaluationDetailDto evaluation,
        IEvaluationRepository repository, CancellationToken ct)
    {
        if (evaluation.Evaluation.EvaluatorId != userId ||
            !await repository.CanEvaluateProjectAsync(userId, evaluation.Evaluation.ProjectId, ct))
            throw new ForbiddenException("Only the assigned human evaluator can modify this evaluation.");
    }
}

public sealed class CreateEvaluationDraftHandler(ICurrentUser user, IEvaluationRepository repository)
    : IRequestHandler<CreateEvaluationDraftCommand, EvaluationDetailDto>
{
    public async Task<EvaluationDetailDto> Handle(CreateEvaluationDraftCommand request, CancellationToken ct)
    {
        var actor = EvaluationAccess.UserId(user);
        if (!await repository.ProjectExistsAsync(request.ProjectId, ct))
            throw new NotFoundException("Project", request.ProjectId);
        if (!await repository.CanEvaluateProjectAsync(actor, request.ProjectId, ct))
            throw new ForbiddenException("Only an evaluator assigned to this project can create an evaluation.");
        var rubric = await repository.GetRubricAsync(request.RubricId, ct)
            ?? throw new NotFoundException("Rubric", request.RubricId);
        if (!rubric.IsActive || rubric.ApprovalStatus != "APPROVED")
            throw new ConflictException("Evaluation must reference an approved active rubric version.");
        var id = await repository.CreateDraftAsync(request.ProjectId, actor, rubric.Id,
            request.EvaluationType.Trim().ToUpperInvariant(), request.EvidenceSummary?.Trim(), ct);
        return await repository.GetAsync(id, ct) ?? throw new NotFoundException("Evaluation", id);
    }
}

public sealed class UpdateEvaluationScoreHandler(ICurrentUser user, IEvaluationRepository repository)
    : IRequestHandler<UpdateEvaluationScoreCommand, Unit>
{
    public async Task<Unit> Handle(UpdateEvaluationScoreCommand request, CancellationToken ct)
    {
        var evaluation = await repository.GetAsync(request.EvaluationId, ct)
            ?? throw new NotFoundException("Evaluation", request.EvaluationId);
        await EvaluationAccess.Ensure(EvaluationAccess.UserId(user), evaluation, repository, ct);
        if (evaluation.Evaluation.Status != "DRAFT") throw new ConflictException("Finalized evaluations are immutable.");
        var criterion = evaluation.Scores.SingleOrDefault(x => x.RubricCriterionId == request.RubricCriterionId)
            ?? throw new NotFoundException("RubricCriterion", request.RubricCriterionId);
        if (request.Score > criterion.MaxScore)
            throw new AIPMS.Application.Common.Exceptions.ValidationException(
                new Dictionary<string, string[]> { ["score"] = [$"Score must be between 0 and {criterion.MaxScore}."] });
        if (!await repository.UpsertScoreAsync(request.EvaluationId, criterion.RubricCriterionId,
            request.Score, request.Comments?.Trim(), ct))
            throw new ConflictException("Evaluation was finalized concurrently.");
        return Unit.Value;
    }
}

public sealed class UpdateEvaluationCommentHandler(ICurrentUser user, IEvaluationRepository repository)
    : IRequestHandler<UpdateEvaluationCommentCommand, Unit>
{
    public async Task<Unit> Handle(UpdateEvaluationCommentCommand request, CancellationToken ct)
    {
        var evaluation = await repository.GetAsync(request.EvaluationId, ct)
            ?? throw new NotFoundException("Evaluation", request.EvaluationId);
        await EvaluationAccess.Ensure(EvaluationAccess.UserId(user), evaluation, repository, ct);
        if (evaluation.Evaluation.Status != "DRAFT") throw new ConflictException("Finalized evaluations are immutable.");
        if (!await repository.UpdateCommentsAsync(request.EvaluationId, request.Comments?.Trim(),
            request.EvidenceSummary?.Trim(), ct))
            throw new ConflictException("Evaluation was finalized concurrently.");
        return Unit.Value;
    }
}

public sealed class FinalizeEvaluationHandler(ICurrentUser user, IEvaluationRepository repository,
    IEvaluationScoreCalculator calculator, TimeProvider clock) : IRequestHandler<FinalizeEvaluationCommand, Unit>
{
    public async Task<Unit> Handle(FinalizeEvaluationCommand request, CancellationToken ct)
    {
        var evaluation = await repository.GetAsync(request.EvaluationId, ct)
            ?? throw new NotFoundException("Evaluation", request.EvaluationId);
        var actor = EvaluationAccess.UserId(user);
        await EvaluationAccess.Ensure(actor, evaluation, repository, ct);
        if (evaluation.Evaluation.Status != "DRAFT") throw new ConflictException("Evaluation is already finalized.");
        if (string.IsNullOrWhiteSpace(evaluation.Evaluation.EvidenceSummary))
            throw new ConflictException("Evidence summary is required before finalization.");
        if (evaluation.Scores.Where(x => x.IsRequired).Any(x => x.Score is null))
            throw new ConflictException("All required rubric criteria must have scores.");
        var scored = evaluation.Scores.Where(x => x.Score.HasValue).ToArray();
        if (scored.Length == 0) throw new ConflictException("At least one criterion must have a score.");
        var total = calculator.Calculate(scored.Select(x =>
            new ScoreInput(x.Score!.Value, x.MaxScore, x.WeightPercent)));
        if (!await repository.FinalizeAsync(request.EvaluationId, total, actor,
            clock.GetUtcNow().UtcDateTime, ct))
            throw new ConflictException("Evaluation was finalized concurrently.");
        return Unit.Value;
    }
}

