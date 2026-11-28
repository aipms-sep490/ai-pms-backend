using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class DefenseSchedulerV4Service
{
    public async Task<StandbyPoolMemberDto> AddStandbyPoolMemberAsync(AddStandbyPoolMemberRequest input, CancellationToken ct)
    {
        var (_, staffDeptId) = await RequireDepartmentStaffAsync(ct);
        if (!await db.ProjectPeriods.AsNoTracking().AnyAsync(p => p.Id == input.ProjectPeriodId, ct))
            throw new NotFoundException("ProjectPeriod", input.ProjectPeriodId);

        var major = await db.Majors.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == input.MajorId && m.IsActive, ct);
        if (major is null || major.DepartmentId != staffDeptId)
            throw new ForbiddenException("Standby pool major must belong to your active department.", WorkflowErrorCodes.ScopeAccessDenied);

        if (input.DepartmentId > 0 && input.DepartmentId != staffDeptId)
            throw new ForbiddenException("Cannot register standby pool members for another department.", WorkflowErrorCodes.ScopeAccessDenied);

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == input.UserId && u.Status == "ACTIVE" && u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer))
            .Select(u => new { u.Id, u.FullName })
            .SingleOrDefaultAsync(ct)
            ?? throw new DomainException("Standby pool member must be an active Lecturer account.", WorkflowErrorCodes.EvaluatorIneligible);

        var existing = await db.Set<DefenseStandbyPoolEntry>()
            .SingleOrDefaultAsync(x => x.ProjectPeriodId == input.ProjectPeriodId && x.UserId == input.UserId && x.MajorId == input.MajorId, ct);
        if (existing is null)
        {
            existing = new DefenseStandbyPoolEntry
            {
                ProjectPeriodId = input.ProjectPeriodId,
                UserId = input.UserId,
                MajorId = input.MajorId,
                DepartmentId = staffDeptId,
                IsAvailable = input.IsAvailable,
                CreatedAt = Now
            };
            db.Set<DefenseStandbyPoolEntry>().Add(existing);
        }
        else
        {
            existing.IsAvailable = input.IsAvailable;
            existing.DepartmentId = staffDeptId;
        }
        await db.SaveChangesAsync(ct);

        return new(existing.Id, existing.ProjectPeriodId, existing.UserId, user.FullName, existing.MajorId, existing.DepartmentId, existing.IsAvailable);
    }

    public async Task<IReadOnlyList<StandbyPoolMemberDto>> ListStandbyPoolAsync(long periodId, CancellationToken ct)
    {
        var (_, staffDeptId) = await RequireDepartmentStaffAsync(ct);
        if (periodId <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["periodId"] = ["Period ID must be a positive integer."] });

        return await (from s in db.Set<DefenseStandbyPoolEntry>().AsNoTracking()
                      join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                      join m in db.Majors.AsNoTracking() on s.MajorId equals m.Id
                      where s.ProjectPeriodId == periodId && s.IsAvailable && m.DepartmentId == staffDeptId
                      orderby s.MajorId, u.FullName
                      select new StandbyPoolMemberDto(s.Id, s.ProjectPeriodId, s.UserId, u.FullName, s.MajorId, s.DepartmentId, s.IsAvailable))
            .ToArrayAsync(ct);
    }

    public async Task<StandbySwapResultDto> StandbySwapAsync(long sessionId, StandbySwapRequest input, CancellationToken ct)
    {
        var (_, staffDeptId) = await RequireDepartmentStaffAsync(ct);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length < 5)
            throw new DomainException("Standby swap reason must be at least 5 characters.", WorkflowErrorCodes.ValidationFailed);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AcquireScheduleLockAsync(ct);

        var session = await db.Set<DefenseSession>().SingleOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("DefenseSession", sessionId);
        await RequireProjectInDepartmentScopeAsync(session.ProjectId, staffDeptId, ct);
        var committee = await db.Set<DefenseCommittee>().Include(c => c.Members)
            .SingleAsync(c => c.Id == session.CommitteeId, ct);

        var replaced = committee.Members.FirstOrDefault(m => m.UserId == input.ReplacedUserId)
            ?? throw new NotFoundException("DefenseCommitteeMember", input.ReplacedUserId);
        if (committee.Members.Any(m => m.UserId == input.ReplacementUserId))
            throw new ConflictException("Replacement evaluator is already on this defense committee.", WorkflowErrorCodes.ScheduleConflict);

        // Verify replacement is in available standby pool for this evaluation period and major
        var semesterId = await db.Projects.AsNoTracking()
            .Where(p => p.Id == session.ProjectId)
            .Select(p => p.Team.AcademicSemesterId)
            .SingleAsync(ct);
        var evalPeriodId = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == "EVALUATION")
            .OrderByDescending(p => p.EnableDefenseRoomScheduling)
            .ThenByDescending(p => p.Status == "ACTIVE")
            .ThenByDescending(p => p.Id)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(ct)
            ?? await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
                .Where(s => s.ProjectId == session.ProjectId)
                .OrderByDescending(s => s.Id)
                .Select(s => (long?)s.ProjectPeriodId)
                .FirstOrDefaultAsync(ct);

        var inPool = evalPeriodId.HasValue && await db.Set<DefenseStandbyPoolEntry>().AsNoTracking()
            .AnyAsync(p => p.ProjectPeriodId == evalPeriodId.Value
                && p.UserId == input.ReplacementUserId
                && p.MajorId == replaced.MajorId
                && p.IsAvailable, ct);
        if (!inPool)
            throw new DomainException("Replacement evaluator is not available in the standby pool for this period and major.", WorkflowErrorCodes.EvaluatorIneligible);

        // D17 COI check on replacement (active or former primary supervisor or discipline mentor)
        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, session.ProjectId, input.ReplacementUserId, ct))
            throw new ConflictException("Replacement evaluator has a supervisor or mentor conflict of interest on this project (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);

        // Check replacement has no overlapping defense session
        var overlapCommittees = await db.Set<DefenseSession>().AsNoTracking()
            .Where(s => s.Id != session.Id && s.Status == "SCHEDULED" && s.StartAt < session.EndAt && s.EndAt > session.StartAt)
            .Select(s => s.CommitteeId).Distinct().ToArrayAsync(ct);
        if (overlapCommittees.Length > 0 && await db.Set<DefenseCommitteeMember>().AsNoTracking()
            .AnyAsync(m => overlapCommittees.Contains(m.CommitteeId) && m.UserId == input.ReplacementUserId, ct))
            throw new ConflictException("Replacement evaluator has an overlapping defense session.", WorkflowErrorCodes.ScheduleConflict);

        // 1. Replace committee member
        var role = replaced.Role;
        var majorId = replaced.MajorId;
        committee.Members.Remove(replaced);
        committee.Members.Add(new DefenseCommitteeMember
        {
            CommitteeId = committee.Id,
            UserId = input.ReplacementUserId,
            MajorId = majorId,
            Role = role
        });
        committee.ConcurrencyToken = Guid.NewGuid();

        // 2. Atomically revoke non-finalized DEFENSE EvaluationAssignments of replaced evaluator & issue to replacement
        var candidateAssignments = await db.Set<EvaluationAssignment>()
            .Where(a => a.ProjectId == session.ProjectId && a.EvaluatorId == input.ReplacedUserId
                && a.Stage == "DEFENSE" && a.Status != "REVOKED" && a.Status != "FINALIZED")
            .ToArrayAsync(ct);
        var assignIds = candidateAssignments.Select(a => a.Id).ToArray();
        var finalizedAssignIds = assignIds.Length == 0
            ? new HashSet<long>()
            : (await (
                from ds in db.Set<EvaluationDraftState>().AsNoTracking()
                join e in db.Evaluations.AsNoTracking() on ds.EvaluationId equals e.Id
                where assignIds.Contains(ds.AssignmentId) && e.Status == "FINALIZED"
                select ds.AssignmentId).Distinct().ToArrayAsync(ct)).ToHashSet();

        var revokedCount = 0;
        var createdCount = 0;
        foreach (var old in candidateAssignments)
        {
            if (finalizedAssignIds.Contains(old.Id)) continue; // Preserve historical finalized evaluations
            old.Status = "REVOKED";
            old.RevokedAt = Now;
            old.RevocationReason = $"Standby swap: {input.Reason.Trim()}";
            old.ConcurrencyToken = Guid.NewGuid();
            revokedCount++;

            db.Set<EvaluationAssignment>().Add(new EvaluationAssignment
            {
                ProjectId = old.ProjectId,
                EvaluatorId = input.ReplacementUserId,
                RubricId = old.RubricId,
                ProjectPeriodId = old.ProjectPeriodId,
                DepartmentId = old.DepartmentId,
                EvaluationType = old.EvaluationType,
                Stage = old.Stage,
                DefenseSessionId = old.DefenseSessionId ?? session.Id,
                Scope = old.Scope,
                MajorId = old.MajorId,
                StudentId = old.StudentId,
                ComponentId = old.ComponentId,
                PolicyVersionId = old.PolicyVersionId,
                ScopeSnapshotJson = old.ScopeSnapshotJson,
                Status = "ACTIVE",
                AssignedBy = Actor,
                AssignedAt = Now,
                ValidFrom = Now,
                ValidUntil = old.ValidUntil,
                ConcurrencyToken = Guid.NewGuid()
            });
            createdCount++;
        }

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(Actor, "DEFENSE_STANDBY_SWAPPED", "DEFENSE_SESSION", session.Id, new Dictionary<string, object?>
        {
            ["committeeId"] = committee.Id,
            ["replacedUserId"] = input.ReplacedUserId,
            ["replacementUserId"] = input.ReplacementUserId,
            ["reason"] = input.Reason.Trim(),
            ["revokedAssignmentsCount"] = revokedCount,
            ["createdAssignmentsCount"] = createdCount
        }), ct);

        await V4WorkflowNotificationHelper.EmitAsync(
            db, Actor, "DEFENSE_STANDBY_SWAPPED",
            $"Hoán đổi thành viên hội đồng bảo vệ (Phiên #{session.Id})",
            $"Thành viên hội đồng cho đồ án #{session.ProjectId} đã được hoán đổi từ giám khảo dự bị. Lý do: {input.Reason.Trim()}",
            "DEFENSE_SESSION", session.Id, [input.ReplacedUserId, input.ReplacementUserId], Now, enqueueEmail: true, ct);

        await tx.CommitAsync(ct);
        return new(session.Id, committee.Id, input.ReplacedUserId, input.ReplacementUserId, revokedCount, createdCount);
    }
}
