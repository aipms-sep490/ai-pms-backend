using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Projects.Commands;

public sealed record RecordDepartmentDecisionCommand(long ProjectId, DepartmentDecisionRequest Decision) : IRequest<ProjectAcademicReviewDto>;

public sealed class RecordDepartmentDecisionCommandHandler(IProjectRepository repository, ICurrentUser currentUser, IAuditTrail audit)
    : IRequestHandler<RecordDepartmentDecisionCommand, ProjectAcademicReviewDto>
{
    public Task<ProjectAcademicReviewDto> Handle(RecordDepartmentDecisionCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async token =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId) throw new UnauthorizedException();
            var result = await repository.RecordDepartmentDecisionAsync(request.ProjectId, actorId, request.Decision, token);
            await audit.RecordAsync(new AuditEntry(actorId, "PROJECT_DEPARTMENT_DECISION", "PROJECT", request.ProjectId,
                new Dictionary<string, object?> { ["snapshotId"] = request.Decision.SnapshotId,
                    ["decision"] = request.Decision.Decision, ["reason"] = request.Decision.Reason }), token);
            return result;
        }, ct);
}

