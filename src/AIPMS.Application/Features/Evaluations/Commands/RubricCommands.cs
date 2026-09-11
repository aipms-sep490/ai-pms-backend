using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Commands;

public sealed record CreateRubricCommand(CreateRubricRequest Input) : IRequest<RubricDto>;
public sealed record UpdateRubricCommand(long Id, UpdateRubricRequest Input) : IRequest<RubricDto>;
public sealed record PublishRubricCommand(long Id, string ConcurrencyToken) : IRequest<RubricDto>;
public sealed record RetireRubricCommand(long Id, string ConcurrencyToken) : IRequest<RubricDto>;
public sealed record CreateRubricVersionCommand(long Id, CreateRubricVersionRequest Input) : IRequest<RubricDto>;
public sealed record DeleteRubricCommand(long Id, string ConcurrencyToken) : IRequest;

public sealed class CreateRubricCommandHandler(RubricWorkflow workflow) : IRequestHandler<CreateRubricCommand, RubricDto>
{
    public Task<RubricDto> Handle(CreateRubricCommand r, CancellationToken ct) => workflow.Create(r.Input, ct);
}
public sealed class UpdateRubricCommandHandler(RubricWorkflow workflow) : IRequestHandler<UpdateRubricCommand, RubricDto>
{
    public Task<RubricDto> Handle(UpdateRubricCommand r, CancellationToken ct) => workflow.Update(r.Id, r.Input, ct);
}
public sealed class PublishRubricCommandHandler(RubricWorkflow workflow) : IRequestHandler<PublishRubricCommand, RubricDto>
{
    public Task<RubricDto> Handle(PublishRubricCommand r, CancellationToken ct) => workflow.ChangeStatus(r.Id, r.ConcurrencyToken, true, ct);
}
public sealed class RetireRubricCommandHandler(RubricWorkflow workflow) : IRequestHandler<RetireRubricCommand, RubricDto>
{
    public Task<RubricDto> Handle(RetireRubricCommand r, CancellationToken ct) => workflow.ChangeStatus(r.Id, r.ConcurrencyToken, false, ct);
}
public sealed class CreateRubricVersionCommandHandler(RubricWorkflow workflow) : IRequestHandler<CreateRubricVersionCommand, RubricDto>
{
    public Task<RubricDto> Handle(CreateRubricVersionCommand r, CancellationToken ct) => workflow.NewVersion(r.Id, r.Input, ct);
}
public sealed class DeleteRubricCommandHandler(RubricWorkflow workflow) : IRequestHandler<DeleteRubricCommand>
{
    public async Task Handle(DeleteRubricCommand r, CancellationToken ct) => await workflow.Delete(r.Id, r.ConcurrencyToken, ct);
}
