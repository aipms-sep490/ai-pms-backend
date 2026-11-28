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

internal sealed partial class AcademicBlueprintService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock,
    IWorkingDayCalculator workingDays,
    IProjectAccessService? projectAccess = null) : IAcademicBlueprintService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly string[] CanonicalPeriodTypes =
    [
        PeriodTypes.Registration,
        PeriodTypes.ProjectReview,
        PeriodTypes.SupervisorSelection,
        PeriodTypes.Execution,
        PeriodTypes.FinalSubmission,
        PeriodTypes.Evaluation
    ];

    private long ActorId => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value
        : throw new UnauthorizedException();

    private async Task<(bool IsAdmin, bool IsStaff, long? DepartmentId, long? OrganizationId)> GetActorScopeAsync(CancellationToken ct)
    {
        var info = await db.Users.AsNoTracking()
            .Where(u => u.Id == ActorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                OrganizationId = u.Department != null ? (long?)u.Department.OrganizationId : null,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        return (info.IsAdmin, info.IsStaff, info.DepartmentId, info.OrganizationId);
    }

    private async Task<long> RequireDepartmentStaffWriteScopeAsync(CancellationToken ct, string? nonAdminMessage = null)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsStaff)
        {
            throw new ForbiddenException(
                actor.IsAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot perform academic blueprint operations."
                    : nonAdminMessage ?? "Only department staff can manage academic blueprints.",
                actor.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : null);
        }

        if (!actor.DepartmentId.HasValue)
            throw new ForbiddenException("Department staff account is not assigned to an active department.");
        return actor.DepartmentId.Value;
    }

    private static AcademicBlueprintConfig ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AcademicBlueprintConfig();
        try { return JsonSerializer.Deserialize<AcademicBlueprintConfig>(json, JsonOpts) ?? new AcademicBlueprintConfig(); }
        catch { return new AcademicBlueprintConfig(); }
    }

    private static AcademicBlueprintDto ToDto(AcademicBlueprint bp) => new(
        bp.Id,
        bp.AcademicSemesterId,
        bp.TrackCode,
        bp.Code,
        bp.Name,
        bp.Version,
        bp.Status,
        bp.LeadDepartmentId,
        bp.ParticipatingDepartmentId,
        bp.PeriodPolicyVersionId,
        bp.GateTemplateId,
        bp.MilestoneTemplateId,
        bp.RubricId,
        ParseConfig(bp.ConfigJson),
        bp.ConcurrencyToken.ToString("N"),
        bp.CreatedBy,
        bp.CreatedAt,
        bp.UpdatedAt,
        bp.PublishedAt,
        bp.Approvals.OrderBy(a => a.ScopeRole).Select(a => new AcademicBlueprintApprovalDto(
            a.DepartmentId, a.ScopeRole, a.Decision, a.DecidedBy, a.DecidedAt, a.Notes)).ToArray());

    private static void ValidateConfigOrThrow(string trackCode, AcademicBlueprintConfig config, bool forPublish)
    {
        var errors = new Dictionary<string, string[]>();
        if (!AcademicTrackCodes.IsValid(trackCode))
            errors["trackCode"] = ["TrackCode must be IT_ONLY, MKT_ONLY, or IT_MKT."];

        var gates = config.GateRules ?? [];
        if (gates.Any(g => g.SubmissionOffsetWorkingDays < 0 || g.ReviewDurationWorkingDays < 0 || g.RevisionDurationWorkingDays < 0))
            errors["gateRules"] = ["Gate working-day offsets and durations cannot be negative."];

        var ordered = gates.OrderBy(g => g.Sequence).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].SubmissionOffsetWorkingDays < ordered[i - 1].SubmissionOffsetWorkingDays)
                errors["gateRules"] = ["Gate submission offsets must follow chronological gate sequence."];
        }

        if (forPublish && trackCode == AcademicTrackCodes.ItMkt)
        {
            var intDeliverables = config.IntegrationDeliverables ?? [];
            var hasValidIntCode = intDeliverables.Any(d =>
                !string.IsNullOrWhiteSpace(d.Code) &&
                d.Code.Trim().StartsWith("INT-", StringComparison.OrdinalIgnoreCase));
            var coversGateAndFinal = intDeliverables.Any(d =>
                d.Stages is not null &&
                d.Stages.Any(s => s.StartsWith("G", StringComparison.OrdinalIgnoreCase)) &&
                d.Stages.Any(s => string.Equals(s, "FINAL", StringComparison.OrdinalIgnoreCase)));
            if (!hasValidIntCode || !coversGateAndFinal)
            {
                throw new DomainException(
                    "Interdisciplinary (IT_MKT) AcademicBlueprint must define at least one INT-* integration deliverable covering Gates (G1-G3) and FINAL.",
                    WorkflowErrorCodes.IntegrationDeliverableRequired);
            }
        }

        if (errors.Count > 0) throw new ValidationException(errors);
    }

    private async Task ValidateGateRequirementsForPublishAsync(AcademicBlueprint bp, AcademicBlueprintConfig config, CancellationToken ct)
    {
        var gates = config.GateRules ?? [];
        if (gates.Count == 0) return;

        ReviewGateTemplate? template = null;
        if (bp.GateTemplateId.HasValue)
        {
            template = await db.ReviewGateTemplates.AsNoTracking()
                .Include(t => t.Gates).ThenInclude(g => g.Requirements)
                .SingleOrDefaultAsync(t => t.Id == bp.GateTemplateId.Value, ct);
        }

        var intDeliverables = config.IntegrationDeliverables ?? [];
        foreach (var gr in gates)
        {
            var hasDeliverables = gr.RequiredDeliverables is not null
                && gr.RequiredDeliverables.Any(d => !string.IsNullOrWhiteSpace(d));
            var hasIntDeliverables = intDeliverables.Any(d =>
                !string.IsNullOrWhiteSpace(d.Code)
                && d.Stages is not null
                && d.Stages.Any(s => string.Equals(s, gr.Code, StringComparison.OrdinalIgnoreCase)));
            var hasTemplateReqs = template is not null
                && template.Gates.Any(g =>
                    (string.Equals(g.Code, gr.Code, StringComparison.OrdinalIgnoreCase) || g.Sequence == gr.Sequence)
                    && g.Requirements.Count > 0);
            if (!hasDeliverables && !hasIntDeliverables && !hasTemplateReqs)
            {
                throw new DomainException(
                    $"Gate '{gr.Code}' must define at least one required deliverable or link a ReviewGateTemplate with requirements before publishing.",
                    WorkflowErrorCodes.GateRequirementsRequired);
            }
        }
    }

    public async Task<IReadOnlyList<AcademicBlueprintDto>> ListAsync(long? semesterId, string? trackCode, string? status, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException("Only department staff or admins can manage academic blueprints.");

        var query = db.AcademicBlueprints.AsNoTracking().Include(b => b.Approvals).AsQueryable();
        if (semesterId.HasValue) query = query.Where(b => b.AcademicSemesterId == semesterId.Value);
        if (!string.IsNullOrWhiteSpace(trackCode)) query = query.Where(b => b.TrackCode == trackCode.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Status == status.Trim().ToUpperInvariant());
        if (!actor.IsAdmin && actor.DepartmentId.HasValue)
            query = query.Where(b => b.LeadDepartmentId == actor.DepartmentId.Value || b.ParticipatingDepartmentId == actor.DepartmentId.Value);

        var list = await query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).ToListAsync(ct);
        return list.Select(ToDto).ToArray();
    }

    public async Task<AcademicBlueprintDto> GetAsync(long id, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        var bp = await db.AcademicBlueprints.AsNoTracking().Include(b => b.Approvals)
            .SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("AcademicBlueprint", id);
        if (!actor.IsAdmin && actor.DepartmentId != bp.LeadDepartmentId && actor.DepartmentId != bp.ParticipatingDepartmentId)
            throw new ForbiddenException("You do not belong to the lead or participating department of this blueprint.");
        return ToDto(bp);
    }

    public async Task<AcademicBlueprintDto> CreateAsync(CreateAcademicBlueprintRequest request, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct, "Only lead department staff can create an academic blueprint.");
        var track = AcademicTrackCodes.Normalize(request.TrackCode)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["trackCode"] = ["TrackCode must be IT_ONLY, MKT_ONLY, or IT_MKT."] });

        if (staffDeptId != request.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff can create an academic blueprint.");

        if (track == AcademicTrackCodes.ItMkt && (!request.ParticipatingDepartmentId.HasValue || request.ParticipatingDepartmentId == request.LeadDepartmentId))
            throw new ValidationException(new Dictionary<string, string[]> { ["participatingDepartmentId"] = ["IT_MKT blueprint requires a distinct participating department."] });

        ValidateConfigOrThrow(track, request.Config, forPublish: false);

        var code = request.Code.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(new Dictionary<string, string[]> { ["code"] = ["Code and Name are required."] });

        if (!await db.AcademicSemesters.AsNoTracking().AnyAsync(s => s.Id == request.AcademicSemesterId, ct))
            throw new NotFoundException("AcademicSemester", request.AcademicSemesterId);

        var now = clock.GetUtcNow().UtcDateTime;
        var bp = new AcademicBlueprint
        {
            AcademicSemesterId = request.AcademicSemesterId,
            TrackCode = track,
            Code = code,
            Name = request.Name.Trim(),
            Version = 1,
            Status = "DRAFT",
            LeadDepartmentId = request.LeadDepartmentId,
            ParticipatingDepartmentId = request.ParticipatingDepartmentId,
            PeriodPolicyVersionId = request.PeriodPolicyVersionId,
            GateTemplateId = request.GateTemplateId,
            MilestoneTemplateId = request.MilestoneTemplateId,
            RubricId = request.RubricId,
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOpts),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = ActorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        bp.Approvals.Add(new AcademicBlueprintApproval { DepartmentId = request.LeadDepartmentId, ScopeRole = "LEAD", Decision = "PENDING" });
        if (request.ParticipatingDepartmentId.HasValue)
            bp.Approvals.Add(new AcademicBlueprintApproval { DepartmentId = request.ParticipatingDepartmentId.Value, ScopeRole = "PARTICIPATING", Decision = "PENDING" });

        db.AcademicBlueprints.Add(bp);
        await db.SaveChangesAsync(ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId, "ACADEMIC_BLUEPRINT_CREATED", "ACADEMIC_BLUEPRINT", bp.Id,
            new Dictionary<string, object?> { ["semesterId"] = bp.AcademicSemesterId, ["trackCode"] = bp.TrackCode, ["code"] = bp.Code }), ct);
        return ToDto(bp);
    }

    public async Task<AcademicBlueprintDto> UpdateAsync(long id, UpdateAcademicBlueprintRequest request, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct);

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (staffDeptId != bp.LeadDepartmentId && staffDeptId != bp.ParticipatingDepartmentId)
            throw new ForbiddenException();

        if (bp.Status is "PUBLISHED" or "RETIRED")
            throw new ConflictException($"AcademicBlueprint v{bp.Version} is {bp.Status} and immutable. Clone a new version instead.", WorkflowErrorCodes.ImmutableVersion);

        if (!Guid.TryParse(request.ConcurrencyToken, out var token) || token != bp.ConcurrencyToken)
            throw new ConflictException("AcademicBlueprint was modified concurrently. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);

        ValidateConfigOrThrow(bp.TrackCode, request.Config, forPublish: false);

        var nextParticipatingDeptId = request.ParticipatingDepartmentId ?? bp.ParticipatingDepartmentId;
        if (bp.TrackCode == AcademicTrackCodes.ItMkt && (!nextParticipatingDeptId.HasValue || nextParticipatingDeptId.Value == bp.LeadDepartmentId))
            throw new ValidationException(new Dictionary<string, string[]> { ["participatingDepartmentId"] = ["IT_MKT blueprint requires a distinct participating department."] });
        if (nextParticipatingDeptId.HasValue && nextParticipatingDeptId.Value == bp.LeadDepartmentId)
            throw new ValidationException(new Dictionary<string, string[]> { ["participatingDepartmentId"] = ["Participating department must be distinct from lead department."] });

        var oldParticipatingDeptId = bp.ParticipatingDepartmentId;
        bp.Name = request.Name.Trim();
        bp.ParticipatingDepartmentId = nextParticipatingDeptId;
        bp.PeriodPolicyVersionId = request.PeriodPolicyVersionId ?? bp.PeriodPolicyVersionId;
        bp.GateTemplateId = request.GateTemplateId ?? bp.GateTemplateId;
        bp.MilestoneTemplateId = request.MilestoneTemplateId ?? bp.MilestoneTemplateId;
        bp.RubricId = request.RubricId ?? bp.RubricId;
        bp.ConfigJson = JsonSerializer.Serialize(request.Config, JsonOpts);
        bp.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        bp.ConcurrencyToken = Guid.NewGuid();

        if (oldParticipatingDeptId != nextParticipatingDeptId)
        {
            var existingPart = bp.Approvals.Where(a => a.ScopeRole == "PARTICIPATING").ToList();
            foreach (var oldRow in existingPart)
            {
                bp.Approvals.Remove(oldRow);
                db.AcademicBlueprintApprovals.Remove(oldRow);
            }
            if (nextParticipatingDeptId.HasValue)
            {
                bp.Approvals.Add(new AcademicBlueprintApproval
                {
                    BlueprintId = bp.Id,
                    DepartmentId = nextParticipatingDeptId.Value,
                    ScopeRole = "PARTICIPATING",
                    Decision = "PENDING"
                });
            }
        }

        // Any edit resets pending sign-offs if already in review
        if (bp.Status == "IN_REVIEW")
        {
            foreach (var approval in bp.Approvals)
            {
                approval.Decision = "PENDING";
                approval.DecidedBy = null;
                approval.DecidedAt = null;
            }
        }

        await db.SaveChangesAsync(ct);
        return ToDto(bp);
    }

    public async Task<AcademicBlueprintDto> CloneVersionAsync(long id, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct, "Only lead department staff can clone a new blueprint version.");

        var source = await db.AcademicBlueprints.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (staffDeptId != source.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff can clone a new blueprint version.");

        var maxVer = await db.AcademicBlueprints.AsNoTracking()
            .Where(b => b.AcademicSemesterId == source.AcademicSemesterId && b.Code == source.Code)
            .MaxAsync(b => b.Version, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var clone = new AcademicBlueprint
        {
            AcademicSemesterId = source.AcademicSemesterId,
            TrackCode = source.TrackCode,
            Code = source.Code,
            Name = source.Name,
            Version = maxVer + 1,
            Status = "DRAFT",
            LeadDepartmentId = source.LeadDepartmentId,
            ParticipatingDepartmentId = source.ParticipatingDepartmentId,
            PeriodPolicyVersionId = source.PeriodPolicyVersionId,
            GateTemplateId = source.GateTemplateId,
            MilestoneTemplateId = source.MilestoneTemplateId,
            RubricId = source.RubricId,
            ConfigJson = source.ConfigJson,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedBy = ActorId,
            CreatedAt = now,
            UpdatedAt = now
        };
        clone.Approvals.Add(new AcademicBlueprintApproval { DepartmentId = clone.LeadDepartmentId, ScopeRole = "LEAD", Decision = "PENDING" });
        if (clone.ParticipatingDepartmentId.HasValue)
            clone.Approvals.Add(new AcademicBlueprintApproval { DepartmentId = clone.ParticipatingDepartmentId.Value, ScopeRole = "PARTICIPATING", Decision = "PENDING" });

        db.AcademicBlueprints.Add(clone);
        await db.SaveChangesAsync(ct);
        return ToDto(clone);
    }

    public async Task<AcademicBlueprintDto> SubmitForReviewAsync(long id, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct);

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (staffDeptId != bp.LeadDepartmentId && staffDeptId != bp.ParticipatingDepartmentId)
            throw new ForbiddenException();
        if (bp.Status is "PUBLISHED" or "RETIRED")
            throw new ConflictException("Published or retired blueprints cannot be submitted for review.", WorkflowErrorCodes.ImmutableVersion);

        bp.Status = "IN_REVIEW";
        bp.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        bp.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(bp);
    }

    public async Task<AcademicBlueprintDto> RecordApprovalAsync(long id, RecordBlueprintApprovalRequest request, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct);
        var decision = (request.Decision ?? "").Trim().ToUpperInvariant();
        if (decision is not ("APPROVED" or "REJECTED"))
            throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Decision must be APPROVED or REJECTED."] });

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (bp.Status is "PUBLISHED" or "RETIRED")
            throw new ConflictException("Published or retired blueprints are immutable.", WorkflowErrorCodes.ImmutableVersion);

        if (staffDeptId != request.DepartmentId)
            throw new ForbiddenException("You can only record approval for your own department.");

        var approval = bp.Approvals.SingleOrDefault(a => a.DepartmentId == request.DepartmentId)
            ?? throw new ForbiddenException("Department is not an approver on this blueprint.");

        var now = clock.GetUtcNow().UtcDateTime;
        approval.Decision = decision;
        approval.DecidedBy = ActorId;
        approval.DecidedAt = now;
        approval.Notes = request.Notes?.Trim();
        if (bp.Status == "DRAFT") bp.Status = "IN_REVIEW";
        bp.UpdatedAt = now;
        bp.ConcurrencyToken = Guid.NewGuid();

        await db.SaveChangesAsync(ct);
        await auditTrail.RecordAsync(new AuditEntry(ActorId, "ACADEMIC_BLUEPRINT_APPROVAL_RECORDED", "ACADEMIC_BLUEPRINT", bp.Id,
            new Dictionary<string, object?> { ["departmentId"] = request.DepartmentId, ["decision"] = decision }), ct);
        return ToDto(bp);
    }

    public async Task<AcademicBlueprintDto> PublishAsync(long id, CancellationToken ct = default)
    {
        var staffDeptId = await RequireDepartmentStaffWriteScopeAsync(ct, "Only lead department staff can publish an academic blueprint.");

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (staffDeptId != bp.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff can publish an academic blueprint.");
        if (bp.Status == "PUBLISHED") return ToDto(bp);
        if (bp.Status == "RETIRED")
            throw new ConflictException("Retired blueprints cannot be published.", WorkflowErrorCodes.ImmutableVersion);

        var config = ParseConfig(bp.ConfigJson);
        ValidateConfigOrThrow(bp.TrackCode, config, forPublish: true);
        await ValidateGateRequirementsForPublishAsync(bp, config, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        if (bp.TrackCode == AcademicTrackCodes.ItMkt)
        {
            var leadOk = bp.Approvals.Any(a => a.ScopeRole == "LEAD" && a.Decision == "APPROVED");
            var partOk = bp.ParticipatingDepartmentId.HasValue &&
                bp.Approvals.Any(a => a.ScopeRole == "PARTICIPATING" && a.DepartmentId == bp.ParticipatingDepartmentId.Value && a.Decision == "APPROVED");
            if (!leadOk || !partOk)
            {
                throw new DomainException(
                    "Interdisciplinary (IT_MKT) blueprint requires APPROVED sign-off from both Lead and Participating departments before publishing.",
                    "BLUEPRINT_DUAL_APPROVAL_REQUIRED");
            }
        }
        else
        {
            var lead = bp.Approvals.SingleOrDefault(a => a.ScopeRole == "LEAD");
            if (lead is not null && lead.Decision == "PENDING")
            {
                lead.Decision = "APPROVED";
                lead.DecidedBy = ActorId;
                lead.DecidedAt = now;
            }
            if (lead is not null && lead.Decision == "REJECTED")
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["approvals"] = ["Cannot publish a blueprint whose lead approval is REJECTED."]
                });
            }
        }

        // Retire any previous published version of the same (semester, track, lead department)
        var priors = await db.AcademicBlueprints
            .Where(b => b.AcademicSemesterId == bp.AcademicSemesterId
                && b.TrackCode == bp.TrackCode
                && b.LeadDepartmentId == bp.LeadDepartmentId
                && b.Status == "PUBLISHED"
                && b.Id != bp.Id)
            .ToListAsync(ct);
        foreach (var prior in priors)
        {
            prior.Status = "RETIRED";
            prior.UpdatedAt = now;
        }

        bp.Status = "PUBLISHED";
        bp.PublishedAt = now;
        bp.UpdatedAt = now;
        bp.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);

        await auditTrail.RecordAsync(new AuditEntry(ActorId, "ACADEMIC_BLUEPRINT_PUBLISHED", "ACADEMIC_BLUEPRINT", bp.Id,
            new Dictionary<string, object?> { ["semesterId"] = bp.AcademicSemesterId, ["trackCode"] = bp.TrackCode, ["version"] = bp.Version }), ct);
        return ToDto(bp);
    }

    public async Task<SemesterReadinessDto> GetSemesterReadinessAsync(long semesterId, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff)
            throw new ForbiddenException("Only department staff or admins can inspect semester blueprint readiness.");

        var semester = await db.AcademicSemesters.AsNoTracking()
            .Where(s => s.Id == semesterId)
            .Select(s => new { s.Id, s.OrganizationId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("AcademicSemester", semesterId);

        if (!actor.IsAdmin && (!actor.OrganizationId.HasValue || actor.OrganizationId.Value != semester.OrganizationId))
            throw new ForbiddenException("You do not belong to the organization of this semester.");

        var periods = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == semesterId && p.Status != SemesterStatuses.Archived)
            .ToListAsync(ct);
        var presentTypes = periods.Select(p => p.PeriodType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingTypes = CanonicalPeriodTypes.Where(t => !presentTypes.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();

        var bpQuery = db.AcademicBlueprints.AsNoTracking()
            .Where(b => b.AcademicSemesterId == semesterId);
        if (!actor.IsAdmin && actor.DepartmentId.HasValue)
            bpQuery = bpQuery.Where(b => b.LeadDepartmentId == actor.DepartmentId.Value || b.ParticipatingDepartmentId == actor.DepartmentId.Value);
        var blueprints = await bpQuery.ToListAsync(ct);
        var publishedTracks = blueprints.Where(b => b.Status == "PUBLISHED").Select(b => b.TrackCode).Distinct().ToArray();
        var configuredTracks = blueprints.Select(b => b.TrackCode).Distinct().ToArray();
        var missingTracks = configuredTracks.Where(t => !publishedTracks.Contains(t)).ToList();

        var issues = new List<string>();
        if (missingTypes.Length > 0)
            issues.Add($"Missing canonical period types: {string.Join(", ", missingTypes)}.");
        if (configuredTracks.Length == 0)
            issues.Add("No AcademicBlueprint configured for this semester.");
        else if (missingTracks.Count > 0)
            issues.Add($"Tracks without a PUBLISHED AcademicBlueprint: {string.Join(", ", missingTracks)}.");

        // Validate chronological sequence across existing periods (REGISTRATION <= EXECUTION <= FINAL_SUBMISSION <= EVALUATION)
        DateTime? PeriodStart(string type) => periods.Where(p => p.PeriodType == type).OrderBy(p => p.StartAt).Select(p => (DateTime?)p.StartAt).FirstOrDefault();
        var regStart = PeriodStart(PeriodTypes.Registration);
        var execStart = PeriodStart(PeriodTypes.Execution);
        var finalStart = PeriodStart(PeriodTypes.FinalSubmission);
        var evalStart = PeriodStart(PeriodTypes.Evaluation);
        if (regStart.HasValue && execStart.HasValue && execStart < regStart)
            issues.Add("EXECUTION period starts before REGISTRATION period.");
        if (execStart.HasValue && finalStart.HasValue && finalStart < execStart)
            issues.Add("FINAL_SUBMISSION period starts before EXECUTION period.");
        if (finalStart.HasValue && evalStart.HasValue && evalStart < finalStart)
            issues.Add("EVALUATION period starts before FINAL_SUBMISSION period.");

        var isReady = missingTracks.Count == 0 && publishedTracks.Length > 0 &&
                      !issues.Any(i => i.Contains("starts before", StringComparison.Ordinal));
        return new SemesterReadinessDto(semesterId, isReady, presentTypes, missingTypes, publishedTracks, missingTracks, issues);
    }

    public async Task EnsureSemesterBlueprintReadyForRegistrationAsync(long semesterId, CancellationToken ct = default)
    {
        var blueprints = await db.AcademicBlueprints.AsNoTracking()
            .Where(b => b.AcademicSemesterId == semesterId)
            .Select(b => new { b.TrackCode, b.Status })
            .ToListAsync(ct);

        // Backward compatibility: legacy semesters that have not opted into AcademicBlueprints are not blocked.
        if (blueprints.Count == 0) return;

        var configuredTracks = blueprints.Select(b => b.TrackCode).Distinct().ToArray();
        var publishedTracks = blueprints.Where(b => b.Status == "PUBLISHED").Select(b => b.TrackCode).Distinct().ToHashSet();
        var unpublished = configuredTracks.Where(t => !publishedTracks.Contains(t)).ToArray();

        if (unpublished.Length > 0)
        {
            throw new ConflictException(
                $"Cannot open REGISTRATION period because track(s) [{string.Join(", ", unpublished)}] do not have a PUBLISHED AcademicBlueprint.",
                WorkflowErrorCodes.BlueprintNotPublished);
        }
    }

    public async Task<IReadOnlyList<SemesterTrackPresetDto>> GetSemesterTrackPresetsAsync(long semesterId, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        var semester = await db.AcademicSemesters.AsNoTracking()
            .Where(s => s.Id == semesterId)
            .Select(s => new { s.Id, s.OrganizationId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("AcademicSemester", semesterId);

        if (!actor.IsAdmin && actor.OrganizationId.HasValue && actor.OrganizationId.Value != semester.OrganizationId)
            throw new ForbiddenException("You do not belong to the organization of this semester.");

        var regPeriod = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == semesterId && p.PeriodType == PeriodTypes.Registration && p.Status != SemesterStatuses.Archived)
            .OrderByDescending(p => p.Status == "ACTIVE" ? 1 : 0)
            .ThenByDescending(p => p.StartAt)
            .FirstOrDefaultAsync(ct);

        var minTeamSize = regPeriod?.MinTeamSize ?? 3;
        var maxTeamSize = regPeriod?.MaxTeamSize ?? 5;
        var minDistinct = regPeriod?.MinDistinctMajors ?? 1;
        var allowedModes = regPeriod?.AllowedProjectModes ?? "SINGLE_MAJOR,INTERDISCIPLINARY";

        var publishedBlueprints = await db.AcademicBlueprints.AsNoTracking()
            .Where(b => b.AcademicSemesterId == semesterId && b.Status == "PUBLISHED")
            .OrderByDescending(b => b.Version)
            .ToListAsync(ct);

        var orgMajors = await db.Majors.AsNoTracking()
            .Where(m => m.IsActive && m.Department.IsActive && m.Department.OrganizationId == semester.OrganizationId)
            .OrderBy(m => m.Id)
            .Select(m => new { m.Id, m.Code, m.Name, m.DepartmentId })
            .ToListAsync(ct);

        bool ModeAllowed(string mode) => ProjectPeriodGovernancePolicy.Allows(allowedModes, mode);

        var presets = new List<SemesterTrackPresetDto>();
        foreach (var track in new[] { AcademicTrackCodes.ItOnly, AcademicTrackCodes.MktOnly, AcademicTrackCodes.ItMkt })
        {
            var projectMode = track == AcademicTrackCodes.ItMkt ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
            if (!ModeAllowed(projectMode)) continue;

            var bp = publishedBlueprints.FirstOrDefault(b => b.TrackCode == track);
            var defaultReqs = new List<SemesterTrackMajorPresetDto>();
            long? leadDeptId = bp?.LeadDepartmentId;
            long? partDeptId = bp?.ParticipatingDepartmentId;

            if (track == AcademicTrackCodes.ItMkt)
            {
                var leadMajor = (leadDeptId.HasValue ? orgMajors.FirstOrDefault(m => m.DepartmentId == leadDeptId.Value) : null)
                    ?? orgMajors.FirstOrDefault();
                var partMajor = (partDeptId.HasValue ? orgMajors.FirstOrDefault(m => m.DepartmentId == partDeptId.Value && m.Id != leadMajor?.Id) : null)
                    ?? orgMajors.FirstOrDefault(m => m.Id != leadMajor?.Id);
                if (leadMajor is not null)
                {
                    leadDeptId ??= leadMajor.DepartmentId;
                    defaultReqs.Add(new SemesterTrackMajorPresetDto(leadMajor.Id, leadMajor.Code, leadMajor.Name, leadMajor.DepartmentId, 1, maxTeamSize, "Chủ trì kỹ thuật / sản phẩm"));
                }
                if (partMajor is not null)
                {
                    partDeptId ??= partMajor.DepartmentId;
                    defaultReqs.Add(new SemesterTrackMajorPresetDto(partMajor.Id, partMajor.Code, partMajor.Name, partMajor.DepartmentId, 1, maxTeamSize, "Phối hợp chuyên ngành"));
                }
            }
            else
            {
                var matchedMajor = (leadDeptId.HasValue ? orgMajors.FirstOrDefault(m => m.DepartmentId == leadDeptId.Value) : null)
                    ?? (track == AcademicTrackCodes.MktOnly
                        ? orgMajors.FirstOrDefault(m => m.Code.Contains("MKT", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Marketing", StringComparison.OrdinalIgnoreCase))
                        : orgMajors.FirstOrDefault(m => !m.Code.Contains("MKT", StringComparison.OrdinalIgnoreCase)))
                    ?? orgMajors.FirstOrDefault();
                if (matchedMajor is not null)
                {
                    leadDeptId ??= matchedMajor.DepartmentId;
                    defaultReqs.Add(new SemesterTrackMajorPresetDto(matchedMajor.Id, matchedMajor.Code, matchedMajor.Name, matchedMajor.DepartmentId, minTeamSize, maxTeamSize, "Chủ trì"));
                }
            }

            presets.Add(new SemesterTrackPresetDto(
                track,
                projectMode,
                minTeamSize,
                maxTeamSize,
                projectMode == "INTERDISCIPLINARY" ? Math.Max(2, minDistinct) : 1,
                leadDeptId,
                partDeptId,
                defaultReqs,
                bp is not null));
        }

        return presets;
    }
}
