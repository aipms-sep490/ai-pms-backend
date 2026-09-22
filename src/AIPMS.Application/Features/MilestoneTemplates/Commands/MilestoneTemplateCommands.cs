using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.MilestoneTemplates.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using MediatR;

namespace AIPMS.Application.Features.MilestoneTemplates.Commands;

public sealed record CreateMilestoneTemplateCommand(string Name, string? Description) : IRequest<MilestoneTemplateDto>;
public sealed record UpdateMilestoneTemplateCommand(long TemplateId, string Name, string? Description) : IRequest;
public sealed record DeleteMilestoneTemplateCommand(long TemplateId) : IRequest;
public sealed record CreateMilestoneTemplateVersionCommand(long TemplateId) : IRequest<MilestoneTemplateVersionDto>;
public sealed record PublishMilestoneTemplateVersionCommand(long VersionId) : IRequest;
public sealed record AddMilestoneTemplateItemCommand(long VersionId, SaveMilestoneTemplateItemRequest Request) : IRequest<MilestoneTemplateVersionDto>;
public sealed record UpdateMilestoneTemplateItemCommand(long ItemId, SaveMilestoneTemplateItemRequest Request) : IRequest<MilestoneTemplateVersionDto>;
public sealed record DeleteMilestoneTemplateItemCommand(long ItemId) : IRequest;
public sealed record AssignMilestoneTemplateCommand(long PeriodId, long TemplateId, long? VersionId = null) : IRequest;

public sealed class CreateMilestoneTemplateHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<CreateMilestoneTemplateCommand, MilestoneTemplateDto>
{
    public Task<MilestoneTemplateDto> Handle(CreateMilestoneTemplateCommand r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationException(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
        return repository.CreateAsync(r.Name.Trim(), r.Description?.Trim(), currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct);
    }
}
public sealed class UpdateMilestoneTemplateHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock) : IRequestHandler<UpdateMilestoneTemplateCommand>
{ public Task Handle(UpdateMilestoneTemplateCommand r, CancellationToken ct) => repository.UpdateAsync(r.TemplateId, r.Name, r.Description, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
public sealed class DeleteMilestoneTemplateHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser) : IRequestHandler<DeleteMilestoneTemplateCommand>
{ public Task Handle(DeleteMilestoneTemplateCommand r, CancellationToken ct) => repository.DeleteAsync(r.TemplateId, currentUser.UserId ?? throw new UnauthorizedException(), ct); }
public sealed class CreateMilestoneTemplateVersionHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<CreateMilestoneTemplateVersionCommand, MilestoneTemplateVersionDto>
{ public Task<MilestoneTemplateVersionDto> Handle(CreateMilestoneTemplateVersionCommand r, CancellationToken ct) => repository.CreateVersionAsync(r.TemplateId, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
public sealed class PublishMilestoneTemplateVersionHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock) : IRequestHandler<PublishMilestoneTemplateVersionCommand>
{ public Task Handle(PublishMilestoneTemplateVersionCommand r, CancellationToken ct) => repository.PublishVersionAsync(r.VersionId, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
public sealed class AddMilestoneTemplateItemHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<AddMilestoneTemplateItemCommand, MilestoneTemplateVersionDto>
{ public Task<MilestoneTemplateVersionDto> Handle(AddMilestoneTemplateItemCommand r, CancellationToken ct) => repository.AddItemAsync(r.VersionId, r.Request, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
public sealed class UpdateMilestoneTemplateItemHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock)
    : IRequestHandler<UpdateMilestoneTemplateItemCommand, MilestoneTemplateVersionDto>
{ public Task<MilestoneTemplateVersionDto> Handle(UpdateMilestoneTemplateItemCommand r, CancellationToken ct) => repository.UpdateItemAsync(r.ItemId, r.Request, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
public sealed class DeleteMilestoneTemplateItemHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser) : IRequestHandler<DeleteMilestoneTemplateItemCommand>
{ public Task Handle(DeleteMilestoneTemplateItemCommand r, CancellationToken ct) => repository.DeleteItemAsync(r.ItemId, currentUser.UserId ?? throw new UnauthorizedException(), ct); }
public sealed class AssignMilestoneTemplateHandler(IMilestoneTemplateRepository repository, ICurrentUser currentUser, TimeProvider clock) : IRequestHandler<AssignMilestoneTemplateCommand>
{ public Task Handle(AssignMilestoneTemplateCommand r, CancellationToken ct) => repository.AssignAsync(r.PeriodId, r.TemplateId, r.VersionId, currentUser.UserId ?? throw new UnauthorizedException(), clock.GetUtcNow().UtcDateTime, ct); }
