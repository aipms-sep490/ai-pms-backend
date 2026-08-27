using System.Security.Cryptography;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.Abstractions;
using AIPMS.Application.Features.Deliverables.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Deliverables.Commands;

public sealed record CreateDeliverableCommand(long ProjectId, long? MilestoneId, string Title, string? Description, string? DeliverableType, DateTime? DueAt) : IRequest<DeliverableDto>;
public sealed record UpdateDeliverableCommand(long Id, string Title, string? Description, string? DeliverableType, DateTime? DueAt) : IRequest<Unit>;
public sealed record DeleteDeliverableCommand(long Id) : IRequest<Unit>;
public sealed record SubmitDeliverableVersionCommand(long DeliverableId, string? Note, string FileName, string ContentType, long FileSize, Stream Content) : IRequest<DeliverableVersionDto>;
public sealed record UploadDeliverableFileCommand(long DeliverableId, string? Note, string FileName, string ContentType, long FileSize, Stream Content) : IRequest<DeliverableVersionDto>;
public sealed record DeleteDeliverableFileCommand(long FileId) : IRequest<Unit>;
public sealed record AddSupervisorFeedbackCommand(long DeliverableVersionId, string FeedbackText) : IRequest<Unit>;
public sealed record RequestDeliverableRevisionCommand(long DeliverableId, string Reason) : IRequest<Unit>;

public sealed class CreateDeliverableCommandHandler(ICurrentUser user, IProjectAccessService access, IDeliverableRepository repo) : IRequestHandler<CreateDeliverableCommand, DeliverableDto>
{ public async Task<DeliverableDto> Handle(CreateDeliverableCommand r, CancellationToken ct) { var id = user.UserId ?? throw new ForbiddenException(); DeliverablePolicy.EnsureCanCreate(user); if (!await repo.ProjectExistsAsync(r.ProjectId,ct)) throw new NotFoundException("Project",r.ProjectId); if(!await access.CanAccessAsync(id,r.ProjectId,ct)) throw new ForbiddenException(); var deliverableId=await repo.CreateAsync(r.ProjectId,r.MilestoneId,r.Title.Trim(),r.Description,r.DeliverableType,r.DueAt,id,ct); return (await repo.GetByIdAsync(deliverableId,ct))!; } }
public sealed class UpdateDeliverableCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo) : IRequestHandler<UpdateDeliverableCommand,Unit>
{ public async Task<Unit> Handle(UpdateDeliverableCommand r,CancellationToken ct) { var d=await repo.GetByIdAsync(r.Id,ct)??throw new NotFoundException("Deliverable",r.Id); var id=user.UserId??throw new ForbiddenException(); if(!await access.CanAccessAsync(id,d.ProjectId,ct))throw new ForbiddenException(); DeliverablePolicy.EnsureCanManage(user,d); DeliverablePolicy.EnsureMutable(d.Status); await repo.UpdateAsync(r.Id,r.Title.Trim(),r.Description,r.DeliverableType,r.DueAt,ct); return Unit.Value;} }
public sealed class DeleteDeliverableCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo) : IRequestHandler<DeleteDeliverableCommand,Unit>
{ public async Task<Unit> Handle(DeleteDeliverableCommand r,CancellationToken ct) { var d=await repo.GetByIdAsync(r.Id,ct)??throw new NotFoundException("Deliverable",r.Id); var id=user.UserId??throw new ForbiddenException();if(!await access.CanAccessAsync(id,d.ProjectId,ct))throw new ForbiddenException();DeliverablePolicy.EnsureCanManage(user,d); DeliverablePolicy.EnsureMutable(d.Status);await repo.DeleteAsync(r.Id,ct);return Unit.Value;} }
public sealed class SubmitDeliverableVersionCommandHandler(ICurrentUser user,IDeliverableRepository repo,IFileStorage storage, TimeProvider clock) : IRequestHandler<SubmitDeliverableVersionCommand,DeliverableVersionDto>
{
    public async Task<DeliverableVersionDto> Handle(SubmitDeliverableVersionCommand r,CancellationToken ct)
    { return await DeliverableFileWorkflow.StoreAsync(r.DeliverableId,r.Note,r.FileName,r.ContentType,r.FileSize,r.Content,user,repo,storage,clock,ct); }
}
public sealed class UploadDeliverableFileCommandHandler(ICurrentUser user,IDeliverableRepository repo,IFileStorage storage,TimeProvider clock):IRequestHandler<UploadDeliverableFileCommand,DeliverableVersionDto>
{ public Task<DeliverableVersionDto> Handle(UploadDeliverableFileCommand r,CancellationToken ct)=>DeliverableFileWorkflow.StoreAsync(r.DeliverableId,r.Note,r.FileName,r.ContentType,r.FileSize,r.Content,user,repo,storage,clock,ct); }
public sealed class DeleteDeliverableFileCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo,IFileStorage storage):IRequestHandler<DeleteDeliverableFileCommand,Unit>
{ public async Task<Unit> Handle(DeleteDeliverableFileCommand r,CancellationToken ct){var f=await repo.GetFileForDownloadAsync(r.FileId,ct)??throw new NotFoundException("File",r.FileId);var id=user.UserId??throw new ForbiddenException();if(!await access.CanAccessAsync(id,f.ProjectId,ct))throw new ForbiddenException();if(f.IsImmutable)throw new ConflictException("Submitted deliverable files are immutable.");await using var source=await storage.OpenReadAsync(f.StorageKey,ct);await using var backup=new MemoryStream();await source.CopyToAsync(backup,ct);await storage.DeleteAsync(f.StorageKey,ct);try{await repo.DeleteUnsubmittedFileAsync(r.FileId,ct);}catch{backup.Position=0;await storage.StoreAsync(f.StorageKey,backup,ct);throw;}return Unit.Value;} }
public sealed class AddSupervisorFeedbackCommandHandler(ICurrentUser user,IDeliverableRepository repo):IRequestHandler<AddSupervisorFeedbackCommand,Unit>
{public async Task<Unit> Handle(AddSupervisorFeedbackCommand r,CancellationToken ct){var id=user.UserId??throw new ForbiddenException();var parent=await repo.GetVersionParentAsync(r.DeliverableVersionId,ct)??throw new NotFoundException("DeliverableVersion",r.DeliverableVersionId);if(!await repo.IsCurrentUserAssignedSupervisorAsync(id,parent.ProjectId,ct))throw new ForbiddenException("Only the active assigned supervisor may give feedback.");var assignment=await repo.GetActiveSupervisorAssignmentIdAsync(parent.ProjectId,ct)??throw new ConflictException("No active supervisor assignment.");await repo.AddFeedbackAsync(r.DeliverableVersionId,assignment,parent.ProjectId,r.FeedbackText.Trim(),ct);return Unit.Value;}}
public sealed class RequestDeliverableRevisionCommandHandler(ICurrentUser user,IDeliverableRepository repo):IRequestHandler<RequestDeliverableRevisionCommand,Unit>
{public async Task<Unit> Handle(RequestDeliverableRevisionCommand r,CancellationToken ct){var id=user.UserId??throw new ForbiddenException();var target=await repo.GetSubmissionTargetAsync(r.DeliverableId,ct)??throw new NotFoundException("Deliverable",r.DeliverableId);if(!await repo.IsCurrentUserAssignedSupervisorAsync(id,target.ProjectId,ct)&&!user.Roles.Contains("ADMIN",StringComparer.OrdinalIgnoreCase))throw new ForbiddenException("Only the active assigned supervisor or an administrator may request revision.");var assignment=await repo.GetActiveSupervisorAssignmentIdAsync(target.ProjectId,ct)??throw new ConflictException("No active supervisor assignment.");var latest=(await repo.GetHistoryAsync(r.DeliverableId,ct)).FirstOrDefault()??throw new ConflictException("A submitted version is required before revision.");await repo.RequestRevisionAsync(r.DeliverableId,latest.Id,assignment,target.ProjectId,r.Reason.Trim(),ct);return Unit.Value;}}

internal static class DeliverableFileWorkflow
{
    private static readonly HashSet<string> Allowed=["application/pdf","application/zip","application/vnd.openxmlformats-officedocument.wordprocessingml.document","application/vnd.openxmlformats-officedocument.presentationml.presentation","image/png","image/jpeg","text/plain"];
    public static async Task EnsureSubmissionAllowedAsync(long userId,SubmissionTarget target,IDeliverableRepository repo,TimeProvider clock,CancellationToken ct){DeliverablePolicy.EnsureMutable(target.DeliverableStatus);if(!await repo.IsProjectActiveAsync(target.ProjectId,ct))throw new ConflictException("Deliverable submission requires an ACTIVE project.");if(!await repo.IsActiveTeamMemberAsync(userId,target.ProjectId,ct))throw new ForbiddenException("Only an active project team member may submit.");if(target.DueAt.HasValue&&clock.GetUtcNow().UtcDateTime>target.DueAt.Value)throw new ConflictException("The deliverable deadline has passed.");}
    public static async Task<DeliverableVersionDto> StoreAsync(long deliverableId,string? note,string fileName,string contentType,long fileSize,Stream content,ICurrentUser user,IDeliverableRepository repo,IFileStorage storage,TimeProvider clock,CancellationToken ct){var id=user.UserId??throw new ForbiddenException();var target=await repo.GetSubmissionTargetAsync(deliverableId,ct)??throw new NotFoundException("Deliverable",deliverableId);await EnsureSubmissionAllowedAsync(id,target,repo,clock,ct);if(fileSize is <=0 or >25*1024*1024||!Allowed.Contains(contentType))throw new ValidationException(new Dictionary<string,string[]>{{"file",["Unsupported file type or file size."]}});if(!content.CanSeek)throw new ValidationException(new Dictionary<string,string[]>{{"file",["The upload stream must be seekable."]}});var safeName=Path.GetFileName(fileName);if(string.IsNullOrWhiteSpace(safeName))throw new ValidationException(new Dictionary<string,string[]>{{"fileName",["A valid file name is required."]}});var checksum=Convert.ToHexString(await SHA256.HashDataAsync(content,ct));content.Position=0;var key=$"deliverables/{target.ProjectId}/{Guid.NewGuid():N}";await storage.StoreAsync(key,content,ct);try{var reviewer=await repo.GetActiveSupervisorAssignmentIdAsync(target.ProjectId,ct);var version=await repo.AddVersionAndFileAsync(new VersionSubmission(deliverableId,0,id,note,safeName,contentType,fileSize,checksum,key,reviewer),ct);return(await repo.GetHistoryAsync(deliverableId,ct)).Single(x=>x.Id==version);}catch{try{await storage.DeleteAsync(key,ct);}catch{/* cleanup is best effort; production storage should retry */}throw;}}
}
internal static class DeliverablePolicy
{
    public static void EnsureCanCreate(ICurrentUser user)
    {
        if (!IsAdmin(user))
            throw new ForbiddenException("Only an administrator may create deliverables.");
    }

    public static void EnsureCanManage(ICurrentUser user, DeliverableDto deliverable)
    {
        if (!IsAdmin(user) && user.UserId != deliverable.CreatedBy)
            throw new ForbiddenException("Only an administrator or the deliverable creator may modify it.");
    }

    public static void EnsureMutable(string status) { if (status is "ACCEPTED" or "CLOSED" or "LOCKED" or "FINAL" or "FINALIZED") throw new ConflictException("Locked or final deliverables require an explicit revision request."); }

    private static bool IsAdmin(ICurrentUser user) => user.Roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase);
}
