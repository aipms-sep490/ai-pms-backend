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
        if (reason.Length is < 50 or > 2000)
            throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["Reason must contain 50 to 2000 characters."] });
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
        if (gate.Status == "PASSED" && gate.BlockNextGate) await OpenNextGate(gate, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_OVERRIDE", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gate.Code,
                ["previousStatus"] = previous, ["newStatus"] = gate.Status, ["reason"] = reason }), token);
        await NotifyGateChanged(projectId, gate.Id, gate.Code, "Gate decision overridden by lead department staff.", token);
        var name = await db.Users.AsNoTracking().Where(u => u.Id == Actor).Select(u => u.FullName).SingleAsync(token);
        return new OverrideReviewGateDto(projectId, gate.Code, gate.Status, gate.DecisionSource,
            name, gate.OverriddenAt.Value, reason, gate.ConcurrencyToken.ToString("N"));
    }, ct);
}
