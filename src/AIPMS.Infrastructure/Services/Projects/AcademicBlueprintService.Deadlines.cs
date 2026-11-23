using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicBlueprints;
using AIPMS.Application.Features.AcademicBlueprints.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Application.Features.Semesters.DTOs;
using AIPMS.Domain.Exceptions;
using AIPMS.Domain.Teams;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class AcademicBlueprintService
{
    public async Task<IReadOnlyList<GateDeadlineChangeRequestDto>> ListDeadlineChangeRequestsAsync(long projectId, string? gateCode = null, CancellationToken ct = default)
    {
        var query = from r in db.GateDeadlineChangeRequests.AsNoTracking()
                    join g in db.ProjectGateInstances.AsNoTracking() on r.GateInstanceId equals g.Id
                    where r.ProjectId == projectId && (gateCode == null || g.Code == gateCode)
                    orderby r.CreatedAt descending, r.Id descending
                    select new GateDeadlineChangeRequestDto(
                        r.Id, r.ProjectId, r.GateInstanceId, g.Code,
                        r.RequestedSubmissionDueAt, r.RequestedReviewEndAt, r.RequestedRevisionDueAt,
                        r.Reason, r.Status, r.RequestedBy, r.ReviewedBy, r.ReviewNotes,
                        r.CreatedAt, r.ReviewedAt, r.ConcurrencyToken.ToString("N"));
        return await query.ToListAsync(ct);
    }

    public async Task<GateDeadlineChangeRequestDto> CreateDeadlineChangeRequestAsync(long projectId, string gateCode, CreateGateDeadlineChangeRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["Reason is required for gate deadline change requests."] });
        if (request.RequestedSubmissionDueAt > request.RequestedReviewEndAt || request.RequestedReviewEndAt > request.RequestedRevisionDueAt)
            throw new DomainException(
                "Gate deadlines must satisfy submissionDueAt <= reviewWindowEndAt <= revisionDueAt.",
                "INVALID_GATE_DEADLINE_ORDER");

        var project = await db.Projects.AsNoTracking().Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);

        var isLeader = project.Team.TeamMembers.Any(m => m.UserId == ActorId && m.LeftAt == null && m.IsLeader);
        var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
            .AnyAsync(sa => sa.ProjectId == projectId && sa.SupervisorProfile.UserId == ActorId && sa.EndedAt == null, ct);
        var actor = await GetActorScopeAsync(ct);
        if (!isLeader && !isSupervisor && !actor.IsAdmin && !actor.IsStaff)
            throw new ForbiddenException("Only the team leader, assigned supervisor, or department staff can request a gate deadline change.");

        var normalizedCode = gateCode.Trim().ToUpperInvariant();
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == normalizedCode, ct)
            ?? throw new NotFoundException("ProjectGateInstance", normalizedCode);

        var now = clock.GetUtcNow().UtcDateTime;
        var entity = new GateDeadlineChangeRequest
        {
            ProjectId = projectId,
            GateInstanceId = gate.Id,
            RequestedSubmissionDueAt = DateTime.SpecifyKind(request.RequestedSubmissionDueAt, DateTimeKind.Utc),
            RequestedReviewEndAt = DateTime.SpecifyKind(request.RequestedReviewEndAt, DateTimeKind.Utc),
            RequestedRevisionDueAt = DateTime.SpecifyKind(request.RequestedRevisionDueAt, DateTimeKind.Utc),
            Reason = request.Reason.Trim(),
            Status = "PENDING",
            RequestedBy = ActorId,
            CreatedAt = now,
            ConcurrencyToken = Guid.NewGuid()
        };
        db.GateDeadlineChangeRequests.Add(entity);
        await db.SaveChangesAsync(ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId, "GATE_DEADLINE_CHANGE_REQUESTED", "PROJECT_GATE_INSTANCE", gate.Id,
            new Dictionary<string, object?> { ["requestId"] = entity.Id, ["projectId"] = projectId, ["gateCode"] = gate.Code }), ct);

        return new GateDeadlineChangeRequestDto(entity.Id, entity.ProjectId, entity.GateInstanceId, gate.Code,
            entity.RequestedSubmissionDueAt, entity.RequestedReviewEndAt, entity.RequestedRevisionDueAt,
            entity.Reason, entity.Status, entity.RequestedBy, entity.ReviewedBy, entity.ReviewNotes,
            entity.CreatedAt, entity.ReviewedAt, entity.ConcurrencyToken.ToString("N"));
    }

    public async Task<GateDeadlineChangeRequestDto> ReviewDeadlineChangeRequestAsync(long projectId, long requestId, ReviewGateDeadlineChangeRequest request, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff)
            throw new ForbiddenException("Only lead department staff or admins can approve or reject gate deadline change requests.");

        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        if (!actor.IsAdmin && scope.LeadDepartmentId.HasValue && actor.DepartmentId != scope.LeadDepartmentId.Value)
            throw new ForbiddenException("Only LeadScope department staff or admins can approve gate deadline changes.");

        var decision = (request.Decision ?? "").Trim().ToUpperInvariant();
        if (decision is not ("APPROVED" or "REJECTED"))
            throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Decision must be APPROVED or REJECTED."] });

        var entity = await db.GateDeadlineChangeRequests
            .SingleOrDefaultAsync(r => r.Id == requestId && r.ProjectId == projectId, ct)
            ?? throw new NotFoundException("GateDeadlineChangeRequest", requestId);

        if (entity.Status != "PENDING")
            throw new ConflictException($"Deadline change request is already {entity.Status}.");

        if (!Guid.TryParse(request.ConcurrencyToken, out var token) || token != entity.ConcurrencyToken)
            throw new ConflictException("Deadline change request was modified concurrently.", WorkflowErrorCodes.StaleConcurrencyToken);

        var gate = await db.ProjectGateInstances.SingleAsync(g => g.Id == entity.GateInstanceId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        entity.Status = decision;
        entity.ReviewedBy = ActorId;
        entity.ReviewedAt = now;
        entity.ReviewNotes = request.ReviewNotes?.Trim();
        entity.ConcurrencyToken = Guid.NewGuid();

        if (decision == "APPROVED")
        {
            gate.SubmissionDueAt = entity.RequestedSubmissionDueAt;
            gate.ReviewWindowEndAt = entity.RequestedReviewEndAt;
            gate.RevisionDueAt = entity.RequestedRevisionDueAt;
            gate.WindowEnd = entity.RequestedRevisionDueAt;
            gate.ConcurrencyToken = Guid.NewGuid();
        }

        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId, "GATE_DEADLINE_CHANGE_REVIEWED", "PROJECT_GATE_INSTANCE", gate.Id,
            new Dictionary<string, object?> { ["requestId"] = entity.Id, ["decision"] = decision, ["gateCode"] = gate.Code }), ct);

        return new GateDeadlineChangeRequestDto(entity.Id, entity.ProjectId, entity.GateInstanceId, gate.Code,
            entity.RequestedSubmissionDueAt, entity.RequestedReviewEndAt, entity.RequestedRevisionDueAt,
            entity.Reason, entity.Status, entity.RequestedBy, entity.ReviewedBy, entity.ReviewNotes,
            entity.CreatedAt, entity.ReviewedAt, entity.ConcurrencyToken.ToString("N"));
    }
}
