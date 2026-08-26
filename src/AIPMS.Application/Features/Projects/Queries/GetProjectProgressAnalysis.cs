using System;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Projects.Queries;

public sealed record GetProjectProgressAnalysisQuery(long ProjectId) : IRequest<ProjectProgressAnalysisDto>;

public sealed class GetProjectProgressAnalysisQueryValidator : AbstractValidator<GetProjectProgressAnalysisQuery>
{
    public GetProjectProgressAnalysisQueryValidator()
    {
        RuleFor(static x => x.ProjectId)
            .GreaterThan(0).WithMessage("ProjectId must be greater than 0.");
    }
}

public sealed class GetProjectProgressAnalysisQueryHandler(
    IProjectProgressDataReader dataReader,
    IProgressAnalysisService aiService,
    IProjectAccessService projectAccessService,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<GetProjectProgressAnalysisQuery, ProjectProgressAnalysisDto>
{
    public async Task<ProjectProgressAnalysisDto> Handle(
        GetProjectProgressAnalysisQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        var actorUserId = currentUser.UserId.Value;

        // Resource Access Check (Prevent IDOR)
        if (!await projectAccessService.CanAccessAsync(actorUserId, request.ProjectId, cancellationToken))
        {
            throw new ForbiddenException("You do not have access to this project's analysis.");
        }

        // Server-Owned Time
        var analysisTimeUtc = timeProvider.GetUtcNow().UtcDateTime;

        // Load Authoritative Facts
        var facts = await dataReader.GetProjectProgressFactsAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);

        // Perform Pure Deterministic Rule Analysis
        var analysisResult = aiService.Analyze(facts, analysisTimeUtc);

        return analysisResult;
    }
}
