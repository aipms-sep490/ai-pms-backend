using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Deliverables.Queries;

public sealed record GetDeliverableQuery(long Id) : IRequest<DeliverableDto>;
public sealed record GetDeliverablesQuery(long ProjectId, int PageNumber, int PageSize) : IRequest<PagedResult<DeliverableDto>>;
public sealed record GetDeliverableHistoryQuery(long DeliverableId) : IRequest<IReadOnlyList<DeliverableVersionDto>>;
public sealed record DownloadDeliverableFileQuery(long FileId) : IRequest<DownloadFileDto>;

public sealed class GetDeliverableQueryHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo):IRequestHandler<GetDeliverableQuery,DeliverableDto>
{ public async Task<DeliverableDto> Handle(GetDeliverableQuery r,CancellationToken ct){var d=await repo.GetByIdAsync(r.Id,ct)??throw new NotFoundException("Deliverable",r.Id);if(!await access.CanAccessAsync(user.UserId??throw new ForbiddenException(),d.ProjectId,ct))throw new ForbiddenException();return d;} }
public sealed class GetDeliverablesQueryHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo):IRequestHandler<GetDeliverablesQuery,PagedResult<DeliverableDto>>
{ public async Task<PagedResult<DeliverableDto>> Handle(GetDeliverablesQuery r,CancellationToken ct){var id=user.UserId??throw new ForbiddenException();if(!await access.CanAccessAsync(id,r.ProjectId,ct))throw new ForbiddenException();return await repo.GetPagedAsync(r.ProjectId,r.PageNumber,r.PageSize,ct);} }
public sealed class GetDeliverableHistoryQueryHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo):IRequestHandler<GetDeliverableHistoryQuery,IReadOnlyList<DeliverableVersionDto>>
{ public async Task<IReadOnlyList<DeliverableVersionDto>> Handle(GetDeliverableHistoryQuery r,CancellationToken ct){var d=await repo.GetByIdAsync(r.DeliverableId,ct)??throw new NotFoundException("Deliverable",r.DeliverableId);if(!await access.CanAccessAsync(user.UserId??throw new ForbiddenException(),d.ProjectId,ct))throw new ForbiddenException();return await repo.GetHistoryAsync(r.DeliverableId,ct);} }
public sealed class DownloadDeliverableFileQueryHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo,IFileStorage storage):IRequestHandler<DownloadDeliverableFileQuery,DownloadFileDto>
{ public async Task<DownloadFileDto> Handle(DownloadDeliverableFileQuery r,CancellationToken ct){var f=await repo.GetFileForDownloadAsync(r.FileId,ct)??throw new NotFoundException("File",r.FileId);if(!await access.CanAccessAsync(user.UserId??throw new ForbiddenException(),f.ProjectId,ct))throw new ForbiddenException();return new DownloadFileDto(f.OriginalFileName,f.MimeType,await storage.OpenReadAsync(f.StorageKey,ct));} }
