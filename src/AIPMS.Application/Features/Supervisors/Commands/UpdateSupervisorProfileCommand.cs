using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using MediatR;

namespace AIPMS.Application.Features.Supervisors.Commands;

public sealed record UpdateSupervisorProfileCommand(long UserId, string? Bio, bool IsAvailable)
    : IRequest<SupervisorProfileDto>;

public sealed class UpdateSupervisorProfileCommandHandler(ISupervisorProfileRepository repository,
    SupervisorAccessService access, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<UpdateSupervisorProfileCommand, SupervisorProfileDto>
{
    public Task<SupervisorProfileDto> Handle(UpdateSupervisorProfileCommand request, CancellationToken ct) =>
        repository.InTransactionAsync(async () =>
        {
            var actor = await access.EnsureCanEditAsync(request.UserId, ct);
            var before = await repository.GetByUserAsync(request.UserId, ct);
            var after = await repository.UpsertAsync(request.UserId,
                string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim(),
                request.IsAvailable, clock.GetUtcNow().UtcDateTime, ct);
            await audit.RecordAsync(new AuditEntry(actor,
                before is null ? "SUPERVISOR_PROFILE_CREATED" : "SUPERVISOR_PROFILE_UPDATED",
                "SUPERVISOR_PROFILE", after.Id, new Dictionary<string, object?>
                {
                    ["before"] = before?.ToDto(), ["after"] = after.ToDto()
                }), ct);
            return after.ToDto();
        }, ct);
}
