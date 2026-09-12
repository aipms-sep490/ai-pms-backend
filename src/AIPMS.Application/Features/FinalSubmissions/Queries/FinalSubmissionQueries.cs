using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Services;
using MediatR;

namespace AIPMS.Application.Features.FinalSubmissions.Queries;

public sealed record GetFinalRequirementsQuery(long ProjectId) : IRequest<FinalRequirementsDto>;
public sealed record GetFinalSubmissionChecklistQuery(long ProjectId) : IRequest<FinalSubmissionChecklistDto>;
public sealed record GetFinalSubmissionQuery(long ProjectId) : IRequest<FinalSubmissionDto>;
public sealed record DownloadFinalSubmissionFileQuery(long ProjectId, long FileId) : IRequest<FileDownload>;
public sealed class GetFinalRequirementsHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<GetFinalRequirementsQuery, FinalRequirementsDto>
{
    public Task<FinalRequirementsDto> Handle(GetFinalRequirementsQuery request, CancellationToken ct) => workflow.Requirements(request.ProjectId, ct);
}
public sealed class GetFinalSubmissionChecklistHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<GetFinalSubmissionChecklistQuery, FinalSubmissionChecklistDto>
{
    public Task<FinalSubmissionChecklistDto> Handle(GetFinalSubmissionChecklistQuery request, CancellationToken ct) => workflow.Checklist(request.ProjectId, ct);
}
public sealed class GetFinalSubmissionHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<GetFinalSubmissionQuery, FinalSubmissionDto>
{
    public Task<FinalSubmissionDto> Handle(GetFinalSubmissionQuery request, CancellationToken ct) => workflow.Get(request.ProjectId, ct);
}
public sealed class DownloadFinalSubmissionFileHandler(FinalSubmissionWorkflow workflow) : IRequestHandler<DownloadFinalSubmissionFileQuery, FileDownload>
{
    public Task<FileDownload> Handle(DownloadFinalSubmissionFileQuery request, CancellationToken ct) => workflow.Download(request.ProjectId, request.FileId, ct);
}
