using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Services;
using MediatR;

namespace AIPMS.Application.Features.Deliverables.Commands;

public sealed record CreateDeliverableCommand(long ProjectId, SaveDeliverableRequest Data) : IRequest<DeliverableDto>;
public sealed record UpdateDeliverableCommand(long Id, SaveDeliverableRequest Data) : IRequest<DeliverableDto>;
public sealed record DeleteDeliverableCommand(long Id) : IRequest<bool>;
public sealed record SubmitDeliverableVersionCommand(long Id, int? ExpectedLatestVersion, string? Note, UploadContent File) : IRequest<DeliverableVersionDto>;
public sealed record ReviewDeliverableVersionCommand(long Id, string Decision, string Feedback) : IRequest<DeliverableFeedbackDto>;
public sealed record UploadProjectFileCommand(string ParentType, long ParentId, UploadContent File) : IRequest<ProjectFileDto>;
public sealed record DeleteProjectFileCommand(long Id) : IRequest<bool>;

public sealed class CreateDeliverableCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<CreateDeliverableCommand, DeliverableDto>
{
    public Task<DeliverableDto> Handle(CreateDeliverableCommand r, CancellationToken ct) => workflow.SaveAsync(null, r.ProjectId, r.Data, ct);
}
public sealed class UpdateDeliverableCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<UpdateDeliverableCommand, DeliverableDto>
{
    public Task<DeliverableDto> Handle(UpdateDeliverableCommand r, CancellationToken ct) => workflow.SaveAsync(r.Id, 0, r.Data, ct);
}
public sealed class DeleteDeliverableCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<DeleteDeliverableCommand, bool>
{
    public Task<bool> Handle(DeleteDeliverableCommand r, CancellationToken ct) => workflow.DeleteAsync(r.Id, ct);
}
public sealed class SubmitDeliverableVersionCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<SubmitDeliverableVersionCommand, DeliverableVersionDto>
{
    public Task<DeliverableVersionDto> Handle(SubmitDeliverableVersionCommand r, CancellationToken ct) => workflow.SubmitAsync(r.Id, r.ExpectedLatestVersion!.Value, r.Note, r.File, ct);
}
public sealed class ReviewDeliverableVersionCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<ReviewDeliverableVersionCommand, DeliverableFeedbackDto>
{
    public Task<DeliverableFeedbackDto> Handle(ReviewDeliverableVersionCommand r, CancellationToken ct) => workflow.ReviewAsync(r.Id, r.Decision, r.Feedback, ct);
}
public sealed class UploadProjectFileCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<UploadProjectFileCommand, ProjectFileDto>
{
    public Task<ProjectFileDto> Handle(UploadProjectFileCommand r, CancellationToken ct) => workflow.AttachAsync(r.ParentType, r.ParentId, r.File, ct);
}
public sealed class DeleteProjectFileCommandHandler(DeliverableWorkflow workflow) : IRequestHandler<DeleteProjectFileCommand, bool>
{
    public Task<bool> Handle(DeleteProjectFileCommand r, CancellationToken ct) => workflow.DeleteFileAsync(r.Id, ct);
}
