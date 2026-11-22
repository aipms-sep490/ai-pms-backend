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

internal sealed class AcademicBlueprintService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail auditTrail,
    TimeProvider clock,
    IWorkingDayCalculator workingDays) : IAcademicBlueprintService
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

    private async Task<(bool IsAdmin, bool IsStaff, long? DepartmentId)> GetActorScopeAsync(CancellationToken ct)
    {
        var info = await db.Users.AsNoTracking()
            .Where(u => u.Id == ActorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")
            })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        return (info.IsAdmin, info.IsStaff, info.DepartmentId);
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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();
        var track = AcademicTrackCodes.Normalize(request.TrackCode)
            ?? throw new ValidationException(new Dictionary<string, string[]> { ["trackCode"] = ["TrackCode must be IT_ONLY, MKT_ONLY, or IT_MKT."] });

        if (!actor.IsAdmin && actor.DepartmentId != request.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff or admin can create an academic blueprint.");

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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (!actor.IsAdmin && actor.DepartmentId != bp.LeadDepartmentId && actor.DepartmentId != bp.ParticipatingDepartmentId)
            throw new ForbiddenException();

        if (bp.Status is "PUBLISHED" or "RETIRED")
            throw new ConflictException($"AcademicBlueprint v{bp.Version} is {bp.Status} and immutable. Clone a new version instead.", WorkflowErrorCodes.ImmutableVersion);

        if (!Guid.TryParse(request.ConcurrencyToken, out var token) || token != bp.ConcurrencyToken)
            throw new ConflictException("AcademicBlueprint was modified concurrently. Reload before retrying.", WorkflowErrorCodes.StaleConcurrencyToken);

        ValidateConfigOrThrow(bp.TrackCode, request.Config, forPublish: false);

        bp.Name = request.Name.Trim();
        bp.ParticipatingDepartmentId = request.ParticipatingDepartmentId ?? bp.ParticipatingDepartmentId;
        bp.PeriodPolicyVersionId = request.PeriodPolicyVersionId ?? bp.PeriodPolicyVersionId;
        bp.GateTemplateId = request.GateTemplateId ?? bp.GateTemplateId;
        bp.MilestoneTemplateId = request.MilestoneTemplateId ?? bp.MilestoneTemplateId;
        bp.RubricId = request.RubricId ?? bp.RubricId;
        bp.ConfigJson = JsonSerializer.Serialize(request.Config, JsonOpts);
        bp.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        bp.ConcurrencyToken = Guid.NewGuid();

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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var source = await db.AcademicBlueprints.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (!actor.IsAdmin && actor.DepartmentId != source.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff or admin can clone a new blueprint version.");

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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (!actor.IsAdmin && actor.DepartmentId != bp.LeadDepartmentId && actor.DepartmentId != bp.ParticipatingDepartmentId)
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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();
        var decision = (request.Decision ?? "").Trim().ToUpperInvariant();
        if (decision is not ("APPROVED" or "REJECTED"))
            throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Decision must be APPROVED or REJECTED."] });

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (bp.Status is "PUBLISHED" or "RETIRED")
            throw new ConflictException("Published or retired blueprints are immutable.", WorkflowErrorCodes.ImmutableVersion);

        if (!actor.IsAdmin && actor.DepartmentId != request.DepartmentId)
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
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff) throw new ForbiddenException();

        var bp = await db.AcademicBlueprints.Include(b => b.Approvals).SingleOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new NotFoundException("AcademicBlueprint", id);
        if (!actor.IsAdmin && actor.DepartmentId != bp.LeadDepartmentId)
            throw new ForbiddenException("Only lead department staff or admin can publish an academic blueprint.");
        if (bp.Status == "PUBLISHED") return ToDto(bp);
        if (bp.Status == "RETIRED")
            throw new ConflictException("Retired blueprints cannot be published.", WorkflowErrorCodes.ImmutableVersion);

        var config = ParseConfig(bp.ConfigJson);
        ValidateConfigOrThrow(bp.TrackCode, config, forPublish: true);

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

        // Retire any previous published version of the same (semester, track, code)
        var priors = await db.AcademicBlueprints
            .Where(b => b.AcademicSemesterId == bp.AcademicSemesterId && b.TrackCode == bp.TrackCode && b.Status == "PUBLISHED" && b.Id != bp.Id)
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
        if (!await db.AcademicSemesters.AsNoTracking().AnyAsync(s => s.Id == semesterId, ct))
            throw new NotFoundException("AcademicSemester", semesterId);

        var periods = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == semesterId && p.Status != SemesterStatuses.Archived)
            .ToListAsync(ct);
        var presentTypes = periods.Select(p => p.PeriodType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missingTypes = CanonicalPeriodTypes.Where(t => !presentTypes.Contains(t, StringComparer.OrdinalIgnoreCase)).ToArray();

        var blueprints = await db.AcademicBlueprints.AsNoTracking()
            .Where(b => b.AcademicSemesterId == semesterId)
            .ToListAsync(ct);
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

    private async Task<(M.Project Project, AcademicBlueprint Blueprint, AcademicBlueprintConfig Config, long? ExecutionPeriodId, DateTime AnchorUtc)>
        ResolveProjectBlueprintContextAsync(long projectId, long? explicitBlueprintId, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.Team).SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);

        var trackCode = AcademicTrackCodes.Normalize(project.TrackCode);
        if (trackCode is null)
        {
            var teamConfig = await db.Set<TeamAcademicConfiguration>().AsNoTracking()
                .SingleOrDefaultAsync(c => c.TeamId == project.TeamId, ct);
            trackCode = AcademicTrackCodes.Normalize(teamConfig?.TrackCode)
                ?? (teamConfig?.ProjectMode == "INTERDISCIPLINARY" ? AcademicTrackCodes.ItMkt : AcademicTrackCodes.ItOnly);
        }

        AcademicBlueprint? bp;
        if (explicitBlueprintId.HasValue)
        {
            bp = await db.AcademicBlueprints.AsNoTracking()
                .SingleOrDefaultAsync(b => b.Id == explicitBlueprintId.Value && b.Status == "PUBLISHED", ct)
                ?? throw new ConflictException("Specified AcademicBlueprint must exist and be PUBLISHED.", WorkflowErrorCodes.BlueprintNotPublished);
        }
        else
        {
            bp = await db.AcademicBlueprints.AsNoTracking()
                .Where(b => b.AcademicSemesterId == project.Team.AcademicSemesterId && b.TrackCode == trackCode && b.Status == "PUBLISHED")
                .OrderByDescending(b => b.Version)
                .FirstOrDefaultAsync(ct);
            if (bp is null)
                throw new ConflictException($"No PUBLISHED AcademicBlueprint found for track {trackCode} in semester {project.Team.AcademicSemesterId}.", WorkflowErrorCodes.BlueprintNotPublished);
        }

        var execPeriod = await db.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == project.Team.AcademicSemesterId && p.PeriodType == PeriodTypes.Execution && p.Status != SemesterStatuses.Archived)
            .OrderBy(p => p.StartAt).ThenBy(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var anchorUtc = execPeriod?.StartAt ?? project.ApprovedAt ?? clock.GetUtcNow().UtcDateTime;
        return (project, bp, ParseConfig(bp.ConfigJson), execPeriod?.Id, DateTime.SpecifyKind(anchorUtc, DateTimeKind.Utc));
    }

    private DateTime ShiftWorkingDays(DateTime fromUtc, int days, long? executionPeriodId)
    {
        if (days <= 0) return fromUtc;
        var dto = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc));
        return executionPeriodId.HasValue
            ? workingDays.AddWorkingDays(dto, days, executionPeriodId.Value).UtcDateTime
            : fromUtc.AddDays(days);
    }

    public async Task<BlueprintDryRunDiffDto> DryRunProjectPlanAsync(long projectId, long? blueprintId = null, CancellationToken ct = default)
    {
        var actor = await GetActorScopeAsync(ct);
        if (!actor.IsAdmin && !actor.IsStaff)
        {
            var isSupervisor = await db.SupervisorAssignments.AsNoTracking()
                .AnyAsync(sa => sa.ProjectId == projectId && sa.SupervisorProfile.UserId == ActorId && sa.EndedAt == null, ct);
            if (!isSupervisor) throw new ForbiddenException("Only department staff, admins, or assigned supervisors can preview blueprint plans.");
        }

        var (_, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);
        var alreadyApplied = await db.ProjectBlueprintSnapshots.AsNoTracking().AnyAsync(s => s.ProjectId == projectId, ct);

        var existingGates = await db.ProjectGateInstances.AsNoTracking().Where(g => g.ProjectId == projectId).ToDictionaryAsync(g => g.Code, ct);
        var existingSprints = await db.Set<Sprint>().AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(ct);
        var existingMilestones = await db.Milestones.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync(ct);
        var existingTasks = await db.Tasks.AsNoTracking().Where(t => t.Milestone.ProjectId == projectId).ToListAsync(ct);

        var preserved = new List<string>();
        var gateDiffs = new List<PlannedGateDiffItem>();
        foreach (var gr in (config.GateRules ?? []).OrderBy(g => g.Sequence))
        {
            var sub = ShiftWorkingDays(anchorUtc, gr.SubmissionOffsetWorkingDays, execPeriodId);
            var revEnd = ShiftWorkingDays(sub, gr.ReviewDurationWorkingDays, execPeriodId);
            var fixDue = ShiftWorkingDays(revEnd, gr.RevisionDurationWorkingDays, execPeriodId);
            var action = existingGates.ContainsKey(gr.Code) ? "UPDATE_DEADLINES" : "CREATE";
            gateDiffs.Add(new PlannedGateDiffItem(gr.Code, gr.Title, action, sub, revEnd, fixDue));
        }

        var sprintDiffs = new List<PlannedScheduleDiffItem>();
        foreach (var sr in config.SprintRules ?? [])
        {
            var start = ShiftWorkingDays(anchorUtc, sr.StartOffsetWorkingDays, execPeriodId);
            var end = ShiftWorkingDays(start, sr.DurationWorkingDays, execPeriodId);
            var match = existingSprints.FirstOrDefault(s => s.TemplateOriginId == sr.OriginId);
            if (match is { IsUserCustomized: true })
            {
                preserved.Add($"SPRINT:{sr.OriginId}:{match.Name}");
                sprintDiffs.Add(new PlannedScheduleDiffItem("SPRINT", sr.OriginId, match.Name, "PRESERVE_CUSTOMIZED", match.StartAt, match.EndAt));
            }
            else
            {
                sprintDiffs.Add(new PlannedScheduleDiffItem("SPRINT", sr.OriginId, sr.Name, match is null ? "CREATE" : "UPDATE", start, end));
            }
        }

        var msDiffs = new List<PlannedScheduleDiffItem>();
        var taskDiffs = new List<PlannedScheduleDiffItem>();
        foreach (var mr in (config.MilestoneRules ?? []).OrderBy(m => m.SortOrder))
        {
            var start = ShiftWorkingDays(anchorUtc, mr.StartOffsetWorkingDays, execPeriodId);
            var due = ShiftWorkingDays(anchorUtc, mr.DueOffsetWorkingDays, execPeriodId);
            var match = existingMilestones.FirstOrDefault(m => m.TemplateOriginId == mr.OriginId);
            if (match is { IsUserCustomized: true })
            {
                preserved.Add($"MILESTONE:{mr.OriginId}:{match.Title}");
                msDiffs.Add(new PlannedScheduleDiffItem("MILESTONE", mr.OriginId, match.Title, "PRESERVE_CUSTOMIZED",
                    match.StartDate?.ToDateTime(TimeOnly.MinValue), match.DueDate?.ToDateTime(TimeOnly.MinValue)));
            }
            else
            {
                msDiffs.Add(new PlannedScheduleDiffItem("MILESTONE", mr.OriginId, mr.Title, match is null ? "CREATE" : "UPDATE", start, due));
            }

            foreach (var tr in mr.CoreTasks ?? [])
            {
                var taskDue = tr.DueOffsetWorkingDays.HasValue ? ShiftWorkingDays(anchorUtc, tr.DueOffsetWorkingDays.Value, execPeriodId) : due;
                var existingTask = existingTasks.FirstOrDefault(t => t.TemplateOriginId == tr.OriginId);
                if (existingTask is { IsUserCustomized: true })
                {
                    preserved.Add($"TASK:{tr.OriginId}:{existingTask.Title}");
                    taskDiffs.Add(new PlannedScheduleDiffItem("TASK", tr.OriginId, existingTask.Title, "PRESERVE_CUSTOMIZED", existingTask.StartAt, existingTask.DueAt, existingTask.IsCoreGateTask));
                }
                else
                {
                    taskDiffs.Add(new PlannedScheduleDiffItem("TASK", tr.OriginId, tr.Title, existingTask is null ? "CREATE" : "UPDATE", start, taskDue, tr.IsCoreGateTask));
                }
            }
        }

        return new BlueprintDryRunDiffDto(projectId, bp.Id, bp.Version, bp.TrackCode, execPeriodId, anchorUtc, alreadyApplied,
            gateDiffs, sprintDiffs, msDiffs, taskDiffs, preserved);
    }

    public async Task<ProjectBlueprintSnapshotDto?> ApplyBlueprintToProjectAsync(long projectId, long actorUserId, DateTime activatedAtUtc, long? blueprintId = null, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        try
        {
            // Lock project row to serialize parallel activations / apply retries
            var locked = await db.Projects
                .FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK, HOLDLOCK) WHERE id = {projectId}")
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw new NotFoundException("Project", projectId);

            var existingSnap = await db.ProjectBlueprintSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(s => s.ProjectId == projectId, ct);
            if (existingSnap is not null)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                return new ProjectBlueprintSnapshotDto(existingSnap.Id, existingSnap.ProjectId, existingSnap.BlueprintId,
                    existingSnap.BlueprintVersion, existingSnap.AppliedBy, existingSnap.AppliedAt, 0, 0, 0, 0, true);
            }

            if (locked.Status != "ACTIVE")
                throw new ConflictException("Only ACTIVE projects can apply an AcademicBlueprint plan.", WorkflowErrorCodes.ProjectNotActive);

            // Try to resolve blueprint; if invoked automatically on activation and none configured, skip gracefully
            AcademicBlueprint? bp;
            AcademicBlueprintConfig config;
            long? execPeriodId;
            DateTime anchorUtc;
            try
            {
                (_, bp, config, execPeriodId, anchorUtc) = await ResolveProjectBlueprintContextAsync(projectId, blueprintId, ct);
            }
            catch (ConflictException ex) when (blueprintId is null && ex.Code == WorkflowErrorCodes.BlueprintNotPublished)
            {
                if (tx is not null) await tx.CommitAsync(ct);
                return null;
            }

            int gatesCreated = 0, sprintsCreated = 0, milestonesCreated = 0, tasksCreated = 0;

            // Ensure a backing ReviewGateTemplate exists if gate rules are provided and blueprint didn't link one
            long? gateTemplateId = bp.GateTemplateId;
            if ((config.GateRules?.Count ?? 0) > 0 && !gateTemplateId.HasValue)
            {
                var autoTplCode = $"BP-{bp.Code}-V{bp.Version}";
                var existingTpl = await db.ReviewGateTemplates.SingleOrDefaultAsync(t => t.Code == autoTplCode && t.Version == bp.Version, ct);
                if (existingTpl is null)
                {
                    existingTpl = new ReviewGateTemplate
                    {
                        Code = autoTplCode,
                        Name = $"{bp.Name} Gate Template",
                        Mode = bp.TrackCode == AcademicTrackCodes.ItMkt ? "INTERDISCIPLINARY" : "SINGLE_MAJOR",
                        Major = bp.TrackCode,
                        Status = "PUBLISHED",
                        Version = bp.Version,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc
                    };
                    db.ReviewGateTemplates.Add(existingTpl);
                    await db.SaveChangesAsync(ct);
                }
                gateTemplateId = existingTpl.Id;
            }

            var existingGates = await db.ProjectGateInstances.Where(g => g.ProjectId == projectId).ToDictionaryAsync(g => g.Code, ct);
            var orderedGates = (config.GateRules ?? []).OrderBy(g => g.Sequence).ToArray();
            for (var i = 0; i < orderedGates.Length; i++)
            {
                var gr = orderedGates[i];
                var sub = ShiftWorkingDays(anchorUtc, gr.SubmissionOffsetWorkingDays, execPeriodId);
                var revEnd = ShiftWorkingDays(sub, gr.ReviewDurationWorkingDays, execPeriodId);
                var fixDue = ShiftWorkingDays(revEnd, gr.RevisionDurationWorkingDays, execPeriodId);
                var offsetJson = JsonSerializer.Serialize(new { gr.SubmissionOffsetWorkingDays, gr.ReviewDurationWorkingDays, gr.RevisionDurationWorkingDays }, JsonOpts);

                if (existingGates.TryGetValue(gr.Code, out var existingGate))
                {
                    existingGate.SubmissionDueAt = sub;
                    existingGate.ReviewWindowEndAt = revEnd;
                    existingGate.RevisionDueAt = fixDue;
                    existingGate.WindowStart ??= anchorUtc;
                    existingGate.WindowEnd = fixDue;
                    existingGate.OffsetRuleJson = offsetJson;
                }
                else if (gateTemplateId.HasValue)
                {
                    db.ProjectGateInstances.Add(new ProjectGateInstance
                    {
                        ProjectId = projectId,
                        TemplateId = gateTemplateId.Value,
                        TemplateVersion = bp.Version,
                        Code = gr.Code,
                        Sequence = gr.Sequence,
                        Title = gr.Title,
                        Objective = gr.Objective,
                        Status = "PLANNED",
                        ReviewEligibility = i == 0 ? "READY" : "BLOCKED",
                        Required = true,
                        BlockNextGate = true,
                        BlocksDefense = gr.BlocksDefense,
                        Weight = gr.Weight,
                        WindowStart = anchorUtc,
                        WindowEnd = fixDue,
                        SubmissionDueAt = sub,
                        ReviewWindowEndAt = revEnd,
                        RevisionDueAt = fixDue,
                        OffsetRuleJson = offsetJson,
                        CreatedAt = activatedAtUtc
                    });
                    gatesCreated++;
                }
            }

            var existingSprints = await db.Set<Sprint>().Where(s => s.ProjectId == projectId).ToListAsync(ct);
            foreach (var sr in config.SprintRules ?? [])
            {
                var start = ShiftWorkingDays(anchorUtc, sr.StartOffsetWorkingDays, execPeriodId);
                var end = ShiftWorkingDays(start, sr.DurationWorkingDays, execPeriodId);
                var match = existingSprints.FirstOrDefault(s => s.TemplateOriginId == sr.OriginId);
                if (match is { IsUserCustomized: true }) continue;
                if (match is null)
                {
                    db.Set<Sprint>().Add(new Sprint
                    {
                        ProjectId = projectId,
                        Name = sr.Name,
                        Status = "PLANNED",
                        StartAt = start,
                        EndAt = end,
                        TemplateOriginId = sr.OriginId,
                        IsUserCustomized = false,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc
                    });
                    sprintsCreated++;
                }
                else
                {
                    match.Name = sr.Name;
                    match.StartAt = start;
                    match.EndAt = end;
                    match.UpdatedAt = activatedAtUtc;
                }
            }

            var existingMilestones = await db.Milestones.Where(m => m.ProjectId == projectId).ToListAsync(ct);
            var existingTasks = await db.Tasks.Where(t => t.Milestone.ProjectId == projectId).ToListAsync(ct);
            foreach (var mr in (config.MilestoneRules ?? []).OrderBy(m => m.SortOrder))
            {
                var start = ShiftWorkingDays(anchorUtc, mr.StartOffsetWorkingDays, execPeriodId);
                var due = ShiftWorkingDays(anchorUtc, mr.DueOffsetWorkingDays, execPeriodId);
                var ms = existingMilestones.FirstOrDefault(m => m.TemplateOriginId == mr.OriginId);
                if (ms is null)
                {
                    ms = new M.Milestone
                    {
                        ProjectId = projectId,
                        Title = mr.Title,
                        Description = mr.Description,
                        StartDate = DateOnly.FromDateTime(start),
                        DueDate = DateOnly.FromDateTime(due),
                        Status = "PLANNED",
                        SortOrder = mr.SortOrder,
                        TemplateOriginId = mr.OriginId,
                        IsUserCustomized = false,
                        CreatedBy = actorUserId,
                        CreatedAt = activatedAtUtc,
                        UpdatedAt = activatedAtUtc,
                        ConcurrencyToken = Guid.NewGuid()
                    };
                    db.Milestones.Add(ms);
                    await db.SaveChangesAsync(ct);
                    existingMilestones.Add(ms);
                    milestonesCreated++;
                }
                else if (!ms.IsUserCustomized)
                {
                    ms.Title = mr.Title;
                    ms.StartDate = DateOnly.FromDateTime(start);
                    ms.DueDate = DateOnly.FromDateTime(due);
                    ms.UpdatedAt = activatedAtUtc;
                }

                foreach (var tr in mr.CoreTasks ?? [])
                {
                    var taskDue = tr.DueOffsetWorkingDays.HasValue ? ShiftWorkingDays(anchorUtc, tr.DueOffsetWorkingDays.Value, execPeriodId) : due;
                    var task = existingTasks.FirstOrDefault(t => t.TemplateOriginId == tr.OriginId);
                    if (task is { IsUserCustomized: true }) continue;
                    if (task is null)
                    {
                        db.Tasks.Add(new M.Task
                        {
                            MilestoneId = ms.Id,
                            Title = tr.Title,
                            Description = tr.Description,
                            Status = "TODO",
                            Priority = tr.Priority,
                            StartAt = start,
                            DueAt = taskDue,
                            TemplateOriginId = tr.OriginId,
                            IsUserCustomized = false,
                            IsCoreGateTask = tr.IsCoreGateTask,
                            CreatedBy = actorUserId,
                            CreatedAt = activatedAtUtc,
                            UpdatedAt = activatedAtUtc,
                            ConcurrencyToken = Guid.NewGuid()
                        });
                        tasksCreated++;
                    }
                    else
                    {
                        task.Title = tr.Title;
                        task.DueAt = taskDue;
                        task.IsCoreGateTask = tr.IsCoreGateTask;
                        task.UpdatedAt = activatedAtUtc;
                    }
                }
            }

            var snap = new ProjectBlueprintSnapshot
            {
                ProjectId = projectId,
                BlueprintId = bp.Id,
                BlueprintVersion = bp.Version,
                SnapshotJson = bp.ConfigJson,
                AppliedBy = actorUserId,
                AppliedAt = activatedAtUtc
            };
            db.ProjectBlueprintSnapshots.Add(snap);
            await db.SaveChangesAsync(ct);

            await auditTrail.RecordAsync(new AuditEntry(actorUserId, "ACADEMIC_BLUEPRINT_APPLIED", "PROJECT", projectId,
                new Dictionary<string, object?> { ["blueprintId"] = bp.Id, ["version"] = bp.Version, ["gatesCreated"] = gatesCreated, ["sprintsCreated"] = sprintsCreated, ["milestonesCreated"] = milestonesCreated, ["tasksCreated"] = tasksCreated }), ct);

            if (tx is not null) await tx.CommitAsync(ct);
            return new ProjectBlueprintSnapshotDto(snap.Id, snap.ProjectId, snap.BlueprintId, snap.BlueprintVersion,
                snap.AppliedBy, snap.AppliedAt, gatesCreated, sprintsCreated, milestonesCreated, tasksCreated, false);
        }
        catch
        {
            if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

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
