using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Services;
using MediatR;

namespace AIPMS.Application.Features.Topics.Commands;

public sealed record CreateTopicCommand(CreateTopicRequest Request) : IRequest<TopicDto>;
public sealed record UpdateTopicCommand(long Id, UpdateTopicRequest Request) : IRequest<TopicDto>;
public sealed record PublishTopicCommand(long Id, PublishTopicRequest Request) : IRequest<TopicDto>;
public sealed record CloseTopicCommand(long Id, CloseTopicRequest Request) : IRequest<TopicDto>;

public sealed class TopicCommandHandler(TopicWorkflow workflow) : IRequestHandler<CreateTopicCommand, TopicDto>,
    IRequestHandler<UpdateTopicCommand, TopicDto>, IRequestHandler<PublishTopicCommand, TopicDto>, IRequestHandler<CloseTopicCommand, TopicDto>
{
    public Task<TopicDto> Handle(CreateTopicCommand request, CancellationToken ct) => workflow.Create(request.Request, ct);
    public Task<TopicDto> Handle(UpdateTopicCommand request, CancellationToken ct) => workflow.Update(request.Id, request.Request, ct);
    public Task<TopicDto> Handle(PublishTopicCommand request, CancellationToken ct) => workflow.ChangeStatus(request.Id, request.Request.ConcurrencyToken, true, null, ct);
    public Task<TopicDto> Handle(CloseTopicCommand request, CancellationToken ct) => workflow.ChangeStatus(request.Id, request.Request.ConcurrencyToken, false, request.Request.Reason, ct);
}
