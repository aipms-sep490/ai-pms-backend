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
public sealed record DeleteDeliverableFileCommand(long FileId) : IRequest<Unit>;
public sealed record AddSupervisorFeedbackCommand(long DeliverableVersionId, string FeedbackText) : IRequest<Unit>;

public sealed class CreateDeliverableCommandHandler(ICurrentUser user, IProjectAccessService access, IDeliverableRepository repo) : IRequestHandler<CreateDeliverableCommand, DeliverableDto>
{ public async Task<DeliverableDto> Handle(CreateDeliverableCommand r, CancellationToken ct) { var id = user.UserId ?? throw new ForbiddenException(); DeliverablePolicy.EnsureCanCreate(user); if (!await repo.ProjectExistsAsync(r.ProjectId,ct)) throw new NotFoundException("Project",r.ProjectId); if(!await access.CanAccessAsync(id,r.ProjectId,ct)) throw new ForbiddenException(); var deliverableId=await repo.CreateAsync(r.ProjectId,r.MilestoneId,r.Title.Trim(),r.Description,r.DeliverableType,r.DueAt,id,ct); return (await repo.GetByIdAsync(deliverableId,ct))!; } }
public sealed class UpdateDeliverableCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo) : IRequestHandler<UpdateDeliverableCommand,Unit>
{ public async Task<Unit> Handle(UpdateDeliverableCommand r,CancellationToken ct) { var d=await repo.GetByIdAsync(r.Id,ct)??throw new NotFoundException("Deliverable",r.Id); var id=user.UserId??throw new ForbiddenException(); if(!await access.CanAccessAsync(id,d.ProjectId,ct))throw new ForbiddenException(); DeliverablePolicy.EnsureCanManage(user,d); DeliverablePolicy.EnsureMutable(d.Status); await repo.UpdateAsync(r.Id,r.Title.Trim(),r.Description,r.DeliverableType,r.DueAt,ct); return Unit.Value;} }
public sealed class DeleteDeliverableCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo) : IRequestHandler<DeleteDeliverableCommand,Unit>
{ public async Task<Unit> Handle(DeleteDeliverableCommand r,CancellationToken ct) { var d=await repo.GetByIdAsync(r.Id,ct)??throw new NotFoundException("Deliverable",r.Id); var id=user.UserId??throw new ForbiddenException();if(!await access.CanAccessAsync(id,d.ProjectId,ct))throw new ForbiddenException();DeliverablePolicy.EnsureCanManage(user,d); DeliverablePolicy.EnsureMutable(d.Status);await repo.DeleteAsync(r.Id,ct);return Unit.Value;} }
public sealed class SubmitDeliverableVersionCommandHandler(ICurrentUser user,IDeliverableRepository repo,IFileStorage storage) : IRequestHandler<SubmitDeliverableVersionCommand,DeliverableVersionDto>
{
    private static readonly HashSet<string> Allowed = ["application/pdf","application/zip","application/vnd.openxmlformats-officedocument.wordprocessingml.document","application/vnd.openxmlformats-officedocument.presentationml.presentation","image/png","image/jpeg","text/plain"];
    public async Task<DeliverableVersionDto> Handle(SubmitDeliverableVersionCommand r,CancellationToken ct)
    { var id=user.UserId??throw new ForbiddenException();var target=await repo.GetSubmissionTargetAsync(r.DeliverableId,ct)??throw new NotFoundException("Deliverable",r.DeliverableId);DeliverablePolicy.EnsureMutable(target.DeliverableStatus);if(!await repo.IsProjectActiveAsync(target.ProjectId,ct))throw new ConflictException("Deliverable submission requires an ACTIVE project.");if(!await repo.IsActiveTeamMemberAsync(id,target.ProjectId,ct))throw new ForbiddenException("Only an active project team member may submit.");if(target.DueAt.HasValue&&DateTime.UtcNow>target.DueAt.Value)throw new ConflictException("The deliverable deadline has passed.");if(r.FileSize is <=0 or > 25*1024*1024||!Allowed.Contains(r.ContentType))throw new ValidationException(new Dictionary<string,string[]> { ["file"] = ["Unsupported file type or file size."] });var key=$"deliverables/{target.ProjectId}/{Guid.NewGuid():N}";if(!r.Content.CanSeek)throw new ValidationException(new Dictionary<string,string[]> { ["file"] = ["The upload stream must be seekable."] });var checksum=Convert.ToHexString(await SHA256.HashDataAsync(r.Content,ct));r.Content.Position=0;await storage.StoreAsync(key,r.Content,ct);try{var version=await repo.AddVersionAndFileAsync(new VersionSubmission(r.DeliverableId,await repo.GetNextVersionNumberAsync(r.DeliverableId,ct),id,r.Note,r.FileName,r.ContentType,r.FileSize,checksum,key,await repo.GetActiveSupervisorAssignmentIdAsync(target.ProjectId,ct)),ct);return (await repo.GetHistoryAsync(r.DeliverableId,ct)).Single(x=>x.Id==version);}catch{try{await storage.DeleteAsync(key,ct);}catch{}throw;}}
}
public sealed class DeleteDeliverableFileCommandHandler(ICurrentUser user,IProjectAccessService access,IDeliverableRepository repo,IFileStorage storage):IRequestHandler<DeleteDeliverableFileCommand,Unit>
{ public async Task<Unit> Handle(DeleteDeliverableFileCommand r,CancellationToken ct){var f=await repo.GetFileForDownloadAsync(r.FileId,ct)??throw new NotFoundException("File",r.FileId);var id=user.UserId??throw new ForbiddenException();if(!await access.CanAccessAsync(id,f.ProjectId,ct))throw new ForbiddenException();if(f.IsImmutable)throw new ConflictException("Submitted deliverable files are immutable.");await storage.DeleteAsync(f.StorageKey,ct);await repo.DeleteUnsubmittedFileAsync(r.FileId,ct);return Unit.Value;} }
public sealed class AddSupervisorFeedbackCommandHandler(ICurrentUser user,IDeliverableRepository repo):IRequestHandler<AddSupervisorFeedbackCommand,Unit>
{public async Task<Unit> Handle(AddSupervisorFeedbackCommand r,CancellationToken ct){var id=user.UserId??throw new ForbiddenException();var parent=await repo.GetVersionParentAsync(r.DeliverableVersionId,ct)??throw new NotFoundException("DeliverableVersion",r.DeliverableVersionId);if(!await repo.IsCurrentUserAssignedSupervisorAsync(id,parent.ProjectId,ct))throw new ForbiddenException("Only the active assigned supervisor may give feedback.");var assignment=await repo.GetActiveSupervisorAssignmentIdAsync(parent.ProjectId,ct)??throw new ConflictException("No active supervisor assignment.");await repo.AddFeedbackAsync(r.DeliverableVersionId,assignment,parent.ProjectId,r.FeedbackText.Trim(),ct);return Unit.Value;}}
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

    public static void EnsureMutable(string status) { if (status is "LOCKED" or "FINAL" or "FINALIZED" or "CLOSED") throw new ConflictException("Locked or final deliverables require an explicit revision policy."); }

    private static bool IsAdmin(ICurrentUser user) => user.Roles.Contains("ADMIN", StringComparer.OrdinalIgnoreCase);
}
