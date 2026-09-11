using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Commands;

public sealed record AssignEvaluatorCommand(long ProjectId, AssignEvaluatorRequest Input) : IRequest<EvaluationAssignmentDto>;
public sealed record RevokeEvaluatorCommand(long Id, RevokeEvaluatorRequest Input) : IRequest<EvaluationAssignmentDto>;
public sealed record CreateEvaluationDraftCommand(long AssignmentId) : IRequest<EvaluationDraftDto>;
public sealed record SaveEvaluationDraftCommand(long Id, SaveEvaluationDraftRequest Input) : IRequest<EvaluationDraftDto>;

public sealed class AssignEvaluatorCommandHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<AssignEvaluatorCommand, EvaluationAssignmentDto>
{
    public Task<EvaluationAssignmentDto> Handle(AssignEvaluatorCommand r, CancellationToken ct) => workflow.Assign(r.ProjectId, r.Input, ct);
}
public sealed class RevokeEvaluatorCommandHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<RevokeEvaluatorCommand, EvaluationAssignmentDto>
{
    public Task<EvaluationAssignmentDto> Handle(RevokeEvaluatorCommand r, CancellationToken ct) => workflow.Revoke(r.Id, r.Input, ct);
}
public sealed class CreateEvaluationDraftCommandHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<CreateEvaluationDraftCommand, EvaluationDraftDto>
{
    public Task<EvaluationDraftDto> Handle(CreateEvaluationDraftCommand r, CancellationToken ct) => workflow.Create(r.AssignmentId, ct);
}
public sealed class SaveEvaluationDraftCommandHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<SaveEvaluationDraftCommand, EvaluationDraftDto>
{
    public Task<EvaluationDraftDto> Handle(SaveEvaluationDraftCommand r, CancellationToken ct) => workflow.Save(r.Id, r.Input, ct);
}
