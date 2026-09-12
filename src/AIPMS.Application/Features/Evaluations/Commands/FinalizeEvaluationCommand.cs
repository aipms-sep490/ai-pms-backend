using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Commands;

public sealed record FinalizeEvaluationCommand(long Id, FinalizeEvaluationRequest Input) : IRequest<EvaluationDraftDto>;
public sealed class FinalizeEvaluationHandler(EvaluationDraftWorkflow workflow) : IRequestHandler<FinalizeEvaluationCommand, EvaluationDraftDto>
{
    public Task<EvaluationDraftDto> Handle(FinalizeEvaluationCommand request, CancellationToken ct) => workflow.Finalize(request.Id, request.Input, ct);
}
