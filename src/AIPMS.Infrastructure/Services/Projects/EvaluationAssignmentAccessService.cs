using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class EvaluationAssignmentAccessService(AipmsDbContext db, ICurrentUser currentUser) : IEvaluationAssignmentAccessService
{
    private async Task<(EvaluationAssignment Row, bool CanScore)> Load(long id, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var row = await db.Set<EvaluationAssignment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("EvaluationAssignment", id);
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("An active account is required.");
        var manager = account.Admin || account.Staff && account.DepartmentId == row.DepartmentId;
        var evaluator = account.Lecturer && row.EvaluatorId == actor && row.Status == "ACTIVE" && account.DepartmentId == row.DepartmentId
            && (row.EvaluationType != "SUPERVISOR" || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == row.ProjectId && a.SupervisorProfile.UserId == actor && a.IsPrimary && a.EndedAt == null, ct));
        if (!manager && !evaluator) throw new ForbiddenException("The assignment is outside your scope.");
        return (row, evaluator && row.Scope is "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
    }

    public async Task<EvaluationAssignmentDetailDto> GetAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, canScore) = await Load(assignmentId, ct);
        var dto = new EvaluationAssignmentDto(row.Id, row.ProjectId, row.EvaluatorId, row.RubricId, row.ProjectPeriodId, row.DepartmentId,
            row.EvaluationType, row.Status, row.AssignedBy, row.AssignedAt, row.RevokedAt, row.ConcurrencyToken.ToString("N"), row.Scope, row.MajorId, row.StudentId, row.ComponentId, row.PolicyVersionId);
        var legacy = row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
        return new(dto, canScore && !legacy, legacy, legacy ? "LEGACY_SCOPE_UNKNOWN" : null);
    }

    public async Task<EvaluationAssignmentEvidenceDto> EvidenceAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, _) = await Load(assignmentId, ct);
        var final = await db.Set<FinalSubmission>().AsNoTracking().Where(x => x.ProjectId == row.ProjectId)
            .Select(x => new { x.Id, x.SubmittedAt, ItemCount = x.Items.Count }).SingleOrDefaultAsync(ct);
        return new(row.Id, row.ProjectId, row.Scope, row.MajorId, row.StudentId, final?.Id, final?.SubmittedAt, final?.ItemCount ?? 0, true);
    }
}
