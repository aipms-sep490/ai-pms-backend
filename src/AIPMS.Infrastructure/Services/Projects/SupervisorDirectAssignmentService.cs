using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Projects;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Application.Features.Supervisors.Services;
using AIPMS.Domain.Supervisors;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;
using Assignment = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorAssignment;
using RequestEntity = AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorRequest;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class SupervisorDirectAssignmentService(
    AipmsDbContext context,
    ISupervisorRequestRepository requests,
    ISupervisorCandidateRepository candidates,
    ISupervisorProfileRepository profiles,
    SupervisorAccessService access,
    IAuditTrail audit,
    TimeProvider clock,
    SupervisorAssignmentWorkflow assignmentWorkflow,
    IProjectRepository projectRepository,
    IProjectActivationService activationService) : ISupervisorDirectAssignmentService
{
    public async Task<PagedResult<SupervisorCandidateDto>> GetDirectCandidatesAsync(
        long projectId,
        string assignmentType,
        long? majorId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var actor = await access.EnsureCanReadAsync(ct);
        RequireDepartmentStaff(actor);

        var normalizedType = NormalizeType(assignmentType, majorId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(context, projectId, ct);
        var authorityDeptId = ResolveAuthorityDepartment(scope, normalizedType, majorId);
        if (authorityDeptId is null || actor.DepartmentId != authorityDeptId.Value)
            throw new ForbiddenException("Only the responsible department staff can list direct supervisor candidates for this slot.");

        var now = clock.GetUtcNow().UtcDateTime;
        var project = await candidates.GetProjectAsync(projectId, now, ct)
            ?? throw new NotFoundException("Project", projectId);
        var policies = await candidates.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, ct, execution: true);
        var semesterLimit = policies.Count > 0 && policies[0].MaxProjectsPerSupervisor is > 0
            ? policies[0].MaxProjectsPerSupervisor!.Value
            : 5;
        var policyPeriodId = policies.Count > 0 ? policies[0].PeriodId : 0;

        var searchDeptIds = normalizedType == "PRIMARY"
            ? project.DepartmentIds
            : new[] { authorityDeptId.Value };
        var result = await candidates.SearchAsync(new SupervisorCandidateSearch(
            projectId,
            project.AcademicSemesterId,
            searchDeptIds,
            semesterLimit,
            search?.Trim(),
            null,
            Math.Max(1, page),
            Math.Clamp(pageSize, 1, 100),
            normalizedType,
            majorId), ct);

        var items = result.Items.Select(c =>
        {
            var effectiveActive = c.ActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0);
            var effectiveSemester = c.SemesterActiveProjects - (c.AlreadyAssignedToProject ? 1 : 0);
            var cap = new SupervisorCapacity(c.ProfileLimit, semesterLimit, Math.Max(0, effectiveActive), Math.Max(0, effectiveSemester));
            return new SupervisorCandidateDto(
                c.Profile.Id,
                c.Profile.UserId,
                c.Profile.FullName,
                c.Profile.DepartmentId,
                c.Profile.DepartmentName,
                c.Profile.Bio,
                c.Profile.Expertise.Select(e => new SupervisorExpertiseDto(e.Name, e.ProficiencyLevel)).ToArray(),
                c.ActiveProjects,
                c.SemesterActiveProjects,
                c.ProfileLimit,
                semesterLimit,
                cap.RemainingSlots,
                policyPeriodId,
                normalizedType,
                majorId,
                authorityDeptId,
                cap.RemainingSlots > 0,
                cap.RemainingSlots > 0 ? [] : [WorkflowErrorCodes.SupervisorCapacityFull]);
        }).ToArray();

        return new PagedResult<SupervisorCandidateDto>(items, result.Page, result.PageSize, result.TotalCount);
    }

    public Task<SupervisorAssignmentDto> DirectAssignAsync(
        long projectId,
        long supervisorProfileId,
        string assignmentType,
        long? majorId,
        string reason,
        CancellationToken ct = default) =>
        requests.InTransactionAsync(async token =>
        {
            var actor = await access.EnsureCanReadAsync(token);
            RequireDepartmentStaff(actor);

            var normalizedType = NormalizeType(assignmentType, majorId);
            var trimmedReason = reason?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedReason))
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["reason"] = ["A reason is required for department direct supervisor assignment."]
                });
            }

            await requests.LockSupervisorAndProjectAsync(supervisorProfileId, projectId, token);

            var scope = await ProjectAcademicScopeReader.ReadAsync(context, projectId, token);
            var authorityDeptId = ResolveAuthorityDepartment(scope, normalizedType, majorId);
            if (authorityDeptId is null || actor.DepartmentId != authorityDeptId.Value)
                throw new ForbiddenException("Only the responsible department staff can directly assign a supervisor for this slot.");

            var now = clock.GetUtcNow().UtcDateTime;
            var project = await candidates.GetProjectAsync(projectId, now, token)
                ?? throw new NotFoundException("Project", projectId);

            if (project.Status is not ("DRAFT" or "REVISION_REQUIRED" or "APPROVED" or "SUPERVISOR_PENDING" or "ACTIVE"))
                throw new ConflictException($"Direct supervisor assignment is not allowed when project status is {project.Status}.");

            if (normalizedType == "PRIMARY" && project.HasActiveAssignment)
                throw new ConflictException("Project already has an active PRIMARY supervisor. Use Replace instead.");

            if (normalizedType == "DISCIPLINE_MENTOR")
            {
                if (project.RequiredMajorIds is null || !project.RequiredMajorIds.Contains(majorId!.Value))
                    throw new ConflictException("The specified major is not required by this project.");
                if (project.OccupiedMentorMajors is not null && project.OccupiedMentorMajors.Contains(majorId.Value))
                    throw new ConflictException("This discipline mentor slot is already occupied. Use Replace instead.");
            }

            // Hard Invariant D17: enforce two-way COI against Gate Reviewer / Council Evaluator / Appeal Reviewer
            await AcademicAssignmentGuard.EnsureSupervisorOrMentorAsync(context, projectId, supervisorProfileId, token);

            var profile = await profiles.GetAsync(supervisorProfileId, token)
                ?? throw new NotFoundException("SupervisorProfile", supervisorProfileId);
            if (!profile.IsAvailable || !project.DepartmentIds.Contains(profile.DepartmentId))
                throw new ConflictException("The selected supervisor is unavailable or outside the project's academic scope.");

            if (normalizedType == "DISCIPLINE_MENTOR")
            {
                var major = project.MajorScopes?.SingleOrDefault(m => m.Id == majorId);
                if (major is null || profile.DepartmentId != major.DepartmentId)
                    throw new ConflictException("The selected discipline mentor must belong to the responsible major's department.");
            }

            var policies = await candidates.GetSelectionPoliciesAsync(project.AcademicSemesterId, now, token, execution: true);
            if (policies.Count == 0 || policies[0].MaxProjectsPerSupervisor is not > 0)
                throw new ConflictException("A supervisor capacity policy is required.");

            var load = await requests.GetWorkloadAsync(supervisorProfileId, project.AcademicSemesterId, token, projectId);
            var capacity = new SupervisorCapacity(load.ProfileLimit, policies[0].MaxProjectsPerSupervisor!.Value,
                load.ActiveProjects, load.SemesterActiveProjects);
            if (capacity.RemainingSlots == 0)
            {
                throw new ConflictException(
                    "The selected supervisor has reached maximum active project capacity.",
                    WorkflowErrorCodes.SupervisorCapacityFull);
            }

            // Cancel any pending student invitations for this same slot
            var pendingSameSlot = await context.SupervisorRequests
                .Where(r => r.ProjectId == projectId && r.Status == "PENDING"
                    && r.AssignmentType == normalizedType && r.MajorId == majorId)
                .ToListAsync(token);
            foreach (var pending in pendingSameSlot)
            {
                pending.Status = "CANCELLED";
                pending.ResponseMessage = "Cancelled due to department direct assignment.";
                pending.RespondedAt = now;
                pending.UpdatedAt = now;
            }

            var directRequest = new RequestEntity
            {
                ProjectId = projectId,
                SupervisorProfileId = supervisorProfileId,
                RequestedBy = actor.UserId,
                Status = "ACCEPTED",
                AssignmentType = normalizedType,
                MajorId = majorId,
                RequestMessage = "Department-authorized direct assignment",
                ResponseMessage = trimmedReason,
                RequestedAt = now,
                RespondedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.SupervisorRequests.Add(directRequest);
            await context.SaveChangesAsync(token);

            var assignment = new Assignment
            {
                ProjectId = projectId,
                SupervisorProfileId = supervisorProfileId,
                SupervisorRequestId = directRequest.Id,
                IsPrimary = normalizedType == "PRIMARY",
                AssignmentType = normalizedType,
                MajorId = majorId,
                AssignedBy = actor.UserId,
                AssignedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.SupervisorAssignments.Add(assignment);
            await context.SaveChangesAsync(token);

            await PolicyVersions.InheritSupervisorAsync(context, projectId, assignment.Id, now, token);

            var projectEntity = await context.Projects.SingleAsync(p => p.Id == projectId, token);
            var oldStatus = projectEntity.Status;
            var activatedNow = false;
            if (projectEntity.Status is "APPROVED" or "SUPERVISOR_PENDING")
            {
                var confirmation = await projectRepository.GetSupervisorConfirmationAsync(projectId, token);
                if (confirmation.HasConfirmedPrimarySupervisor && confirmation.MissingMentorMajorIds.Count == 0)
                {
                    var wa = await WorkingAgreementGuard.Read(context, projectId, token);
                    if (!wa.Required || wa.Status == "READY")
                    {
                        projectEntity.Status = "ACTIVE";
                        projectEntity.UpdatedAt = now;
                        context.ProjectStatusHistories.Add(new Persistence.Generated.Models.ProjectStatusHistory
                        {
                            ProjectId = projectId,
                            OldStatus = oldStatus,
                            NewStatus = "ACTIVE",
                            ChangedBy = actor.UserId,
                            Reason = $"Activated after department direct assignment ({trimmedReason}).",
                            ChangedAt = now
                        });
                        await context.SaveChangesAsync(token);
                        await activationService.ApplyMilestoneTemplateAsync(projectId, actor.UserId, now, token);
                        activatedNow = true;
                    }
                    else if (projectEntity.Status == "APPROVED")
                    {
                        projectEntity.Status = "SUPERVISOR_PENDING";
                        projectEntity.UpdatedAt = now;
                        context.ProjectStatusHistories.Add(new Persistence.Generated.Models.ProjectStatusHistory
                        {
                            ProjectId = projectId,
                            OldStatus = "APPROVED",
                            NewStatus = "SUPERVISOR_PENDING",
                            ChangedBy = actor.UserId,
                            Reason = "Supervision assigned directly; awaiting working agreement signatures.",
                            ChangedAt = now
                        });
                        await context.SaveChangesAsync(token);
                    }
                }
            }

            var assignedDto = await context.SupervisorAssignments.AsNoTracking()
                .Where(a => a.Id == assignment.Id)
                .Select(SupervisorAssignmentMapper.Projection)
                .SingleAsync(token);

            await audit.RecordAsync(new AuditEntry(
                actor.UserId,
                "SUPERVISOR_DIRECTLY_ASSIGNED",
                "SUPERVISOR_ASSIGNMENT",
                assignment.Id,
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["supervisorProfileId"] = supervisorProfileId,
                    ["assignmentType"] = normalizedType,
                    ["majorId"] = majorId,
                    ["reason"] = trimmedReason,
                    ["activatedProject"] = activatedNow,
                    ["after"] = assignedDto.ToDto()
                }), token);

            if (activatedNow)
            {
                await audit.RecordAsync(new AuditEntry(
                    actor.UserId,
                    "PROJECT_ACTIVATED",
                    "PROJECT",
                    projectId,
                    new Dictionary<string, object?>
                    {
                        ["oldStatus"] = oldStatus,
                        ["newStatus"] = "ACTIVE",
                        ["source"] = "SUPERVISOR_DIRECTLY_ASSIGNED"
                    }), token);
            }

            // Notify assigned lecturer and all active team students (In-App + Email)
            var studentIds = await V4WorkflowNotificationHelper.GetActiveTeamStudentIdsAsync(context, projectId, token);
            var roleLabel = normalizedType == "PRIMARY" ? "Giảng viên hướng dẫn chính" : "Giảng viên hướng dẫn chuyên ngành";
            await V4WorkflowNotificationHelper.EmitAsync(
                context,
                actor.UserId,
                "SUPERVISOR_DIRECTLY_ASSIGNED",
                $"Khoa đã phân công {roleLabel} cho đồ án {projectEntity.Code}",
                $"Giảng viên {profile.FullName} được phân công làm {roleLabel} cho đồ án {projectEntity.Title}. Lý do: {trimmedReason}",
                "PROJECT",
                projectId,
                studentIds.Append(profile.UserId),
                now,
                enqueueEmail: true,
                token);

            return await assignmentWorkflow.GetAsync(assignment.Id, token);
        }, ct);

    private static void RequireDepartmentStaff(SupervisorAccount actor)
    {
        if (actor.HasActiveAcademicScope && actor.Roles.Contains(AppRoles.DepartmentStaff) && actor.DepartmentId.HasValue)
            return;
        var pureAdmin = actor.Roles.Contains(AppRoles.Admin) && !actor.Roles.Contains(AppRoles.DepartmentStaff);
        throw new ForbiddenException(
            pureAdmin
                ? "Platform administrators without DEPARTMENT_STAFF role cannot directly assign supervisors."
                : "Only active department staff can directly assign supervisors.",
            pureAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
    }

    private static string NormalizeType(string assignmentType, long? majorId)
    {
        var normalized = (assignmentType ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not ("PRIMARY" or "DISCIPLINE_MENTOR")
            || (normalized == "PRIMARY" && majorId.HasValue)
            || (normalized == "DISCIPLINE_MENTOR" && majorId is not > 0))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["assignmentType"] = ["PRIMARY requires no majorId; DISCIPLINE_MENTOR requires a positive majorId."]
            });
        }
        return normalized;
    }

    private static long? ResolveAuthorityDepartment(ProjectAcademicScope scope, string normalizedType, long? majorId)
    {
        if (normalizedType == "PRIMARY") return scope.LeadDepartmentId;
        if (majorId is long mId && scope.MajorDepartmentIds is not null && scope.MajorDepartmentIds.TryGetValue(mId, out var deptId))
            return deptId;
        return null;
    }
}
