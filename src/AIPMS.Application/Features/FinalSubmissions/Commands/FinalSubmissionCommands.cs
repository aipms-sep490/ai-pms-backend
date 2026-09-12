using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Services;
using MediatR;

namespace AIPMS.Application.Features.FinalSubmissions.Commands;

public sealed record ConfigureFinalRequirementsCommand(long ProjectId, ConfigureFinalRequirementsRequest Input) : IRequest<FinalRequirementsDto>;
public sealed record SubmitFinalSubmissionCommand(long ProjectId, SubmitFinalSubmissionRequest Input) : IRequest<FinalSubmissionDto>;
public sealed class ConfigureFinalRequirementsHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<ConfigureFinalRequirementsCommand, FinalRequirementsDto>
{
    public Task<FinalRequirementsDto> Handle(ConfigureFinalRequirementsCommand request, CancellationToken ct) => workflow.Configure(request.ProjectId, request.Input, ct);
}
public sealed class SubmitFinalSubmissionHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<SubmitFinalSubmissionCommand, FinalSubmissionDto>
{
    public Task<FinalSubmissionDto> Handle(SubmitFinalSubmissionCommand request, CancellationToken ct) => workflow.Submit(request.ProjectId, request.Input, ct);
}
