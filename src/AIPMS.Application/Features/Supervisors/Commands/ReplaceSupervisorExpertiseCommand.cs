using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands;

public sealed record ReplaceSupervisorExpertiseCommand(long ProfileId, IReadOnlyList<SupervisorExpertiseDto> Expertise)
    : IRequest<SupervisorProfileDto>;

public sealed class ReplaceSupervisorExpertiseCommandHandler(ISupervisorProfileRepository repository,
    SupervisorAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<ReplaceSupervisorExpertiseCommand, SupervisorProfileDto>
{
    public Task<SupervisorProfileDto> Handle(ReplaceSupervisorExpertiseCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            await access.EnsureCanReadAsync(ct);
            var before = await repository.GetAsync(request.ProfileId, ct)
                ?? throw new NotFoundException("SupervisorProfile", request.ProfileId);
            var actor = await access.EnsureCanEditAsync(before.UserId, ct);
            var items = request.Expertise.Select(e => new SupervisorExpertiseModel(e.Name.Trim(),
                string.IsNullOrWhiteSpace(e.ProficiencyLevel) ? null : e.ProficiencyLevel.Trim())).ToArray();
            var after = await repository.ReplaceExpertiseAsync(request.ProfileId, items, clock.GetUtcNow().UtcDateTime, ct);
            await audit.RecordAsync(new AuditEntry(actor, "SUPERVISOR_EXPERTISE_UPDATED", "SUPERVISOR_PROFILE",
                after.Id, new Dictionary<string, object?> { ["before"] = before.Expertise, ["after"] = after.Expertise }), ct);
            return after.ToDto();
        }, ct);
}
