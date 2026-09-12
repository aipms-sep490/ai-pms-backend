using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Services;
using MediatR;

namespace AIPMS.Application.Features.FinalSubmissions.Commands;

public sealed record CreateFinalSubmissionDraftCommand(long ProjectId, CreateFinalSubmissionDraftRequest Input) : IRequest<FinalSubmissionDraftDto>;
public sealed record UpdateFinalSubmissionDraftCommand(long ProjectId, UpdateFinalSubmissionDraftRequest Input) : IRequest<FinalSubmissionDraftDto>;

public sealed class CreateFinalSubmissionDraftHandler(FinalSubmissionDraftWorkflow workflow)
    : IRequestHandler<CreateFinalSubmissionDraftCommand, FinalSubmissionDraftDto>
{
    public Task<FinalSubmissionDraftDto> Handle(CreateFinalSubmissionDraftCommand request, CancellationToken ct) =>
        workflow.Create(request.ProjectId, request.Input, ct);
}
public sealed class UpdateFinalSubmissionDraftHandler(FinalSubmissionDraftWorkflow workflow)
    : IRequestHandler<UpdateFinalSubmissionDraftCommand, FinalSubmissionDraftDto>
{
    public Task<FinalSubmissionDraftDto> Handle(UpdateFinalSubmissionDraftCommand request, CancellationToken ct) =>
        workflow.Update(request.ProjectId, request.Input, ct);
}
