using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Events;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Domain.Supervisors;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Assignment = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorAssignment;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class SupervisorReplacementService(AipmsDbContext context, ISupervisorRequestRepository requests,
    ISupervisorCandidateRepository candidates, ISupervisorProfileRepository profiles,
    SupervisorAccessService access, IAuditTrail audit, TimeProvider clock, IPublisher events) : ISupervisorReplacementService
{
    public Task<SupervisorAssignmentDto> ReplaceAsync(long assignmentId, long supervisorProfileId, string reason, CancellationToken ct) =>
        requests.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            if (!actor.HasActiveAcademicScope || !actor.Roles.Contains(AppRoles.DepartmentStaff))
                throw new ForbiddenException("Only department staff can replace a supervisor.");
            var old = await context.SupervisorAssignments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assignmentId, token)
                ?? throw new NotFoundException("SupervisorAssignment", assignmentId);
            // Lock profiles in a stable order before the project, including the outgoing capacity slot.
            foreach (var profileId in new[] { old.SupervisorProfileId, supervisorProfileId }.Distinct().Order())
                await context.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.supervisor_profiles WITH (UPDLOCK, HOLDLOCK) WHERE id = {profileId}").ToListAsync(token);
            await requests.LockSupervisorAndProjectAsync(supervisorProfileId, old.ProjectId, token);
            old = await context.SupervisorAssignments.SingleAsync(a => a.Id == assignmentId, token);
            var scope = await ProjectAcademicScopeReader.ReadAsync(context, old.ProjectId, token);
            var authority = old.IsPrimary ? scope.LeadDepartmentId
                : await context.Majors.Where(m => m.Id == old.MajorId && scope.MajorIds.Contains(m.Id))
                    .Select(m => (long?)m.DepartmentId).SingleOrDefaultAsync(token);
            if (!old.IsPrimary && old.MajorId is long frozenMajor && scope.MajorDepartmentIds is not null)
                authority = scope.MajorDepartmentIds.TryGetValue(frozenMajor, out var frozenDepartment) ? frozenDepartment : null;
            if (authority is null || actor.DepartmentId != authority || !scope.DepartmentIds.Contains(authority.Value))
                throw new ForbiddenException("Only the responsible department can replace this assignment.");
            var priorReplacement = await context.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ReplacesAssignmentId == assignmentId).Select(SupervisorAssignmentMapper.Projection).SingleOrDefaultAsync(token);
            if (priorReplacement is not null)
            {
                if (priorReplacement.SupervisorProfileId == supervisorProfileId && old.EndReason == reason.Trim())
                    return priorReplacement.ToDto();
                throw new ConflictException("This assignment was already replaced.");
            }
            var now = clock.GetUtcNow().UtcDateTime;
            var project = await candidates.GetProjectAsync(old.ProjectId, now, token)
                ?? throw new NotFoundException("Project", old.ProjectId);
            if (old.EndedAt is not null || old.SupervisorProfileId == supervisorProfileId
                || project.Status != "ACTIVE" || !project.HasActiveSemester || !project.HasActiveAssignment || now < old.AssignedAt)
                throw new ConflictException("Replacement requires an active assignment and a different supervisor on an ACTIVE project.");
            var profile = await profiles.GetAsync(supervisorProfileId, token)
                ?? throw new NotFoundException("SupervisorProfile", supervisorProfileId);
            if (!profile.IsAvailable || !project.DepartmentIds.Contains(profile.DepartmentId))
                throw new ConflictException("The replacement supervisor is unavailable or outside the project scope.");
            if (!old.IsPrimary)
            {
                var major = project.MajorScopes?.SingleOrDefault(m => m.Id == old.MajorId);
                if (major is null || profile.DepartmentId != major.DepartmentId
                    || !profile.Expertise.Any(e => string.Equals(e.Name, major.Name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(e.Name, major.Code, StringComparison.OrdinalIgnoreCase)))
                    throw new ConflictException("The replacement mentor must have the required major expertise and department.");
            }
            var policies = await candidates.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, token, execution: true);
            if (policies.Count != 1 || policies[0].MaxProjectsPerSupervisor is not > 0)
                throw new ConflictException("A supervisor capacity policy is required.");
            var load = await requests.GetWorkloadAsync(supervisorProfileId, project.AcademicSemesterId, token, old.ProjectId);
            if (new SupervisorCapacity(load.ProfileLimit, policies[0].MaxProjectsPerSupervisor!.Value,
                load.ActiveProjects, load.SemesterActiveProjects).RemainingSlots == 0)
                throw new ConflictException("The replacement supervisor has reached capacity.");
            old.EndedAt = now; old.EndedBy = actor.UserId; old.EndReason = reason.Trim(); old.UpdatedAt = now;
            await context.SaveChangesAsync(token);
            // The request records the staff decision explicitly; it does not impersonate lecturer acceptance.
            var request = new Persistence.Generated.Models.SupervisorRequest
            {
                ProjectId = old.ProjectId, SupervisorProfileId = supervisorProfileId, RequestedBy = actor.UserId,
                Status = "ACCEPTED", AssignmentType = old.AssignmentType, MajorId = old.MajorId,
                RequestMessage = "Department-authorized replacement", ResponseMessage = reason.Trim(),
                RequestedAt = now, RespondedAt = now, CreatedAt = now, UpdatedAt = now
            };
            context.SupervisorRequests.Add(request);
            await context.SaveChangesAsync(token);
            var replacement = new Assignment
            {
                ProjectId = old.ProjectId, SupervisorProfileId = supervisorProfileId, SupervisorRequestId = request.Id,
                IsPrimary = old.IsPrimary, AssignmentType = old.AssignmentType, MajorId = old.MajorId,
                AssignedAt = now, AssignedBy = actor.UserId, ReplacesAssignmentId = old.Id, CreatedAt = now, UpdatedAt = now
            };
            context.SupervisorAssignments.Add(replacement);
            await context.SaveChangesAsync(token);
            var result = await context.SupervisorAssignments.AsNoTracking().Where(a => a.Id == replacement.Id)
                .Select(SupervisorAssignmentMapper.Projection).SingleAsync(token);
            await audit.RecordAsync(new AuditEntry(actor.UserId, "SUPERVISOR_REPLACED", "SUPERVISOR_ASSIGNMENT", replacement.Id,
                new Dictionary<string, object?> { ["previousAssignmentId"] = old.Id, ["after"] = result.ToDto(), ["reason"] = reason.Trim() }), token);
            await events.Publish(new WorkflowNotificationEvent(WorkflowNotificationKind.SupervisorReplaced, replacement.Id, actor.UserId, now), token);
            return result.ToDto();
        }, ct);
}
