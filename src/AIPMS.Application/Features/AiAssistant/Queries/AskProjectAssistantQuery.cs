using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Features.AiAssistant.Abstractions;
using AIPMS.Application.Features.AiAssistant.DTOs;
using MediatR;

namespace AIPMS.Application.Features.AiAssistant.Queries;

public sealed record AskProjectAssistantQuery(long ProjectId, string Query) : IRequest<ProjectAssistantResponseDto>;

public sealed class AskProjectAssistantQueryHandler(IAiAssistantService aiAssistantService)
    : IRequestHandler<AskProjectAssistantQuery, ProjectAssistantResponseDto>
{
    public Task<ProjectAssistantResponseDto> Handle(AskProjectAssistantQuery request, CancellationToken cancellationToken) =>
        aiAssistantService.AskAsync(request.ProjectId, request.Query, cancellationToken);
}
