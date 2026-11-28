using AIPMS.Application.Common.Exceptions;
using System.Threading.Tasks;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ReviewGateService
{
    public async Task<ReviewGateUserCapabilitiesDto> OverrideCapability(long projectId, CancellationToken ct)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct))
            throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, true);
        await RequireProjectRead(projectId, scope, ct);
        var allowed = scope.LeadDepartmentId.HasValue && await db.Users.AsNoTracking().AnyAsync(u =>
            u.Id == Actor && u.Status == "ACTIVE" && u.DepartmentId == scope.LeadDepartmentId
            && u.Department != null && u.Department.IsActive
            && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), ct);
        return new(projectId, [new ExecutionActionDto("CAN_OVERRIDE_GATE", allowed,
            allowed ? [] : ["LEAD_DEPARTMENT_STAFF_REQUIRED"])]);
    }

    public Task<OverrideReviewGateDto> OverrideDecision(long projectId, string gateCode,
        OverrideReviewGateRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        if (!scope.LeadDepartmentId.HasValue || !await db.Users.AsNoTracking().AnyAsync(u =>
                u.Id == Actor && u.Status == "ACTIVE" && u.DepartmentId == scope.LeadDepartmentId
                && u.Department != null && u.Department.IsActive
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), token))
            throw new ForbiddenException("Only staff of the project's lead department may override a gate.");
        if (input.Decision is not ("PASSED" or "REVISION_REQUIRED" or "FAILED"))
            throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Unsupported gate decision."] });
        var reason = input.Reason?.Trim() ?? "";
        if (reason.Length is < 20 or > 2000)
            throw new AIPMS.Domain.Exceptions.DomainException(
                "Override reason must contain 20 to 2000 characters.",
                WorkflowErrorCodes.OverrideReasonTooShort);
        var gate = await db.ProjectGateInstances.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token)
            ?? throw new NotFoundException("ProjectGate", 0);
        CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
        var previous = gate.Status;
        gate.Status = input.Decision;
        gate.ReviewEligibility = input.Decision == "PASSED" ? "READY" : "BLOCKED";
        gate.DecisionSource = "OVERRIDE";
        gate.OverriddenBy = Actor;
        gate.OverriddenAt = Now;
        gate.OverrideReason = reason;
        gate.ConcurrencyToken = Guid.NewGuid();
        var activeAssignments = await db.GateReviewerAssignments
            .Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED" && a.Status != "DECIDED")
            .ToArrayAsync(token);
        foreach (var assignment in activeAssignments)
        {
            assignment.Status = "REVOKED";
            assignment.RevokedBy = Actor;
            assignment.RevokedAt = Now;
            assignment.RevocationReason = "Gate decision overridden by lead department staff.";
            assignment.ConcurrencyToken = Guid.NewGuid();
        }
        await db.SaveChangesAsync(token);
        if (gate.Status == "PASSED") await OpenNextGate(gate, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_OVERRIDE", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gate.Code,
                ["previousStatus"] = previous, ["newStatus"] = gate.Status, ["reason"] = reason }), token);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == Actor).Select(u => u.FullName).SingleAsync(token);
        await NotifyGateChanged(projectId, gate.Id, gate.Code, $"{name} đã ghi đè quyết định cổng {gate.Code} sang trạng thái: {GateStatusLabel(gate.Status)}.", token, enqueueEmail: true, includeDepartmentStaff: true);
        return new OverrideReviewGateDto(projectId, gate.Code, gate.Status, gate.DecisionSource,
            name, gate.OverriddenAt.Value, reason, gate.ConcurrencyToken.ToString("N"));
    }, ct);

    public Task<StudentReviewTimelineDto> ReopenGate(long projectId, string gateCode,
        ReopenReviewGateRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        if (!scope.LeadDepartmentId.HasValue || !await db.Users.AsNoTracking().AnyAsync(u =>
                u.Id == Actor && u.Status == "ACTIVE" && u.DepartmentId == scope.LeadDepartmentId
                && u.Department != null && u.Department.IsActive
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), token))
            throw new ForbiddenException("Only staff of the project's lead department may reopen a review gate.", WorkflowErrorCodes.ScopeAccessDenied);

        var reason = input.Reason?.Trim() ?? "";
        if (reason.Length is < 20 or > 2000)
            throw new AIPMS.Domain.Exceptions.DomainException(
                "Reopening a review gate requires an audit reason between 20 and 2000 characters.",
                WorkflowErrorCodes.OverrideReasonTooShort);

        await AcademicAssignmentGuard.LockProjectAsync(db, projectId, token);
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, token)
            ?? throw new NotFoundException("Project", projectId);
        if (project.Status != "ACTIVE")
            throw new ConflictException("Review gates can only be reopened while the project is ACTIVE.", WorkflowErrorCodes.ProjectNotActive);

        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token)
            ?? throw new NotFoundException("ProjectGate", 0);
        CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);

        var previous = gate.Status;
        ResetOverride(gate);
        gate.Status = "REVISION_REQUIRED";
        gate.ReviewEligibility = "READY";
        gate.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        await ReopenAssignmentsForRevision(gate, token);

        await audit.RecordAsync(new(Actor, "REVIEW_GATE_REOPENED", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gate.Code,
                ["previousStatus"] = previous, ["reason"] = reason }), token);

        var reviewerIds = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED")
            .Select(a => a.ReviewerId).Distinct().ToArrayAsync(token);
        var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(db, projectId, token);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, Actor, "REVIEW_GATE_REOPENED",
            $"Khoa chủ trì đã mở lại cổng {gate.Code}",
            $"Cổng {gate.Code} được mở lại để bổ sung minh chứng. Lý do: {reason}",
            "PROJECT_GATE", gate.Id, studentIds.Concat(reviewerIds), Now, enqueueEmail: false, token);

        var fresh = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence)
            .Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
        return await TimelineDto(project, scope, fresh, token);
    }, ct);
}
