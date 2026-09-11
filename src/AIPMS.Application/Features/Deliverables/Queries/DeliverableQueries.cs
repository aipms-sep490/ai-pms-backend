using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Services;
using MediatR;

namespace AIPMS.Application.Features.Deliverables.Queries;

public sealed record GetDeliverableQuery(long Id) : IRequest<DeliverableDto>;
public sealed record GetDeliverablesQuery(long ProjectId, string? Status, string? Search, int Page = 1, int PageSize = 20,
    long? MilestoneId = null, string? DeliverableType = null) : IRequest<PagedResult<DeliverableDto>>;
public sealed record GetDeliverableVersionQuery(long Id) : IRequest<DeliverableVersionDto>;
public sealed record GetDeliverableVersionsQuery(long Id, int Page = 1, int PageSize = 20) : IRequest<PagedResult<DeliverableVersionDto>>;
public sealed record GetDeliverableFeedbackQuery(long Id, int Page = 1, int PageSize = 20) : IRequest<PagedResult<DeliverableFeedbackDto>>;
public sealed record GetProjectFilesQuery(long ProjectId, string? Search, int Page = 1, int PageSize = 20,
    string? ContentType = null, long? UploadedBy = null, DateTime? From = null, DateTime? To = null,
    string? ParentType = null, long? ParentId = null) : IRequest<PagedResult<ProjectFileDto>>;
public sealed record GetProjectFileQuery(long Id) : IRequest<ProjectFileDto>;
public sealed record DownloadProjectFileQuery(long Id) : IRequest<FileDownload>;

public sealed class GetDeliverableQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetDeliverableQuery, DeliverableDto>
{
    public Task<DeliverableDto> Handle(GetDeliverableQuery r, CancellationToken ct) => workflow.GetAsync(r.Id, ct);
}
public sealed class GetDeliverablesQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetDeliverablesQuery, PagedResult<DeliverableDto>>
{
    public Task<PagedResult<DeliverableDto>> Handle(GetDeliverablesQuery r, CancellationToken ct) =>
        workflow.ListAsync(new(r.ProjectId, r.Status, r.Search, r.Page, r.PageSize, r.MilestoneId, r.DeliverableType), ct);
}
public sealed class GetDeliverableVersionQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetDeliverableVersionQuery, DeliverableVersionDto>
{
    public Task<DeliverableVersionDto> Handle(GetDeliverableVersionQuery r, CancellationToken ct) => workflow.VersionAsync(r.Id, ct);
}
public sealed class GetDeliverableVersionsQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetDeliverableVersionsQuery, PagedResult<DeliverableVersionDto>>
{
    public Task<PagedResult<DeliverableVersionDto>> Handle(GetDeliverableVersionsQuery r, CancellationToken ct) => workflow.VersionsAsync(r.Id, r.Page, r.PageSize, ct);
}
public sealed class GetDeliverableFeedbackQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetDeliverableFeedbackQuery, PagedResult<DeliverableFeedbackDto>>
{
    public Task<PagedResult<DeliverableFeedbackDto>> Handle(GetDeliverableFeedbackQuery r, CancellationToken ct) => workflow.FeedbackAsync(r.Id, r.Page, r.PageSize, ct);
}
public sealed class GetProjectFilesQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetProjectFilesQuery, PagedResult<ProjectFileDto>>
{
    public Task<PagedResult<ProjectFileDto>> Handle(GetProjectFilesQuery r, CancellationToken ct) =>
        workflow.FilesAsync(new(r.ProjectId, r.Search, r.Page, r.PageSize, r.ContentType, r.UploadedBy, r.From, r.To, r.ParentType, r.ParentId), ct);
}
public sealed class GetProjectFileQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<GetProjectFileQuery, ProjectFileDto>
{
    public Task<ProjectFileDto> Handle(GetProjectFileQuery r, CancellationToken ct) => workflow.FileAsync(r.Id, ct);
}
public sealed class DownloadProjectFileQueryHandler(DeliverableWorkflow workflow) : IRequestHandler<DownloadProjectFileQuery, FileDownload>
{
    public Task<FileDownload> Handle(DownloadProjectFileQuery r, CancellationToken ct) => workflow.DownloadAsync(r.Id, ct);
}
