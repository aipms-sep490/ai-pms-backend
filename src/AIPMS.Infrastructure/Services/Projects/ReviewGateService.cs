using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Services;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ReviewGateService(AipmsDbContext db, ICurrentUser current, IProjectRepository transactions,
    IAuditTrail audit, TimeProvider clock, IFileStorage storage) : IReviewGateService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<bool> IsStaff(CancellationToken ct) => await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE" &&
        u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN" || r.Role.Code == "DEPARTMENT_STAFF"), ct);
    private async Task<bool> IsAdmin(CancellationToken ct) => await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE" &&
        u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct);

    private async Task RequireStaff(CancellationToken ct)
    { if (!await IsStaff(ct)) throw new ForbiddenException("Academic staff access is required."); }

    private static void Page(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw new ValidationException(new Dictionary<string, string[]> { ["page"] = ["Invalid bounded pagination."] }); }

    public async Task<IReadOnlyList<ReviewGateTemplateDto>> Templates(CancellationToken ct)
    {
        await RequireStaff(ct);
        var rows = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements)
            .OrderByDescending(x => x.UpdatedAt).ToArrayAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<ReviewGateTemplateDto?> Template(long id, CancellationToken ct)
    {
        await RequireStaff(ct);
        var row = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public Task<ReviewGateTemplateDto> SaveTemplate(SaveReviewGateTemplateRequest input, long? id, CancellationToken ct) =>
        transactions.InTransactionAsync(async token =>
        {
            await RequireStaff(token);
            if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name) || input.Gates is null || input.Gates.Count == 0)
                throw new ValidationException(new Dictionary<string, string[]> { ["template"] = ["Code, name and at least one gate are required."] });
            if (input.Mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) throw new ValidationException(new Dictionary<string, string[]> { ["mode"] = ["Unsupported project mode."] });
            ReviewGateTemplate row;
            if (id.HasValue)
            {
                row = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == id, token)
                    ?? throw new NotFoundException("ReviewGateTemplate", id.Value);
                if (row.Status == "PUBLISHED") throw new ConflictException("Published templates are immutable; create a new version.");
                if (!string.IsNullOrWhiteSpace(input.ConcurrencyToken) && (!Guid.TryParse(input.ConcurrencyToken, out var tokenValue) || tokenValue != row.ConcurrencyToken))
                    throw new ConflictException("The template changed; reload before retrying.");
                db.ReviewGateRequirements.RemoveRange(row.Gates.SelectMany(x => x.Requirements));
                db.ReviewGateDefinitions.RemoveRange(row.Gates);
            }
            else row = new ReviewGateTemplate { CreatedBy = Actor, CreatedAt = Now };
            row.ProjectPeriodId = input.ProjectPeriodId; row.Code = input.Code.Trim(); row.Name = input.Name.Trim();
            if (!int.TryParse(input.Version.Split('.', 2)[0], out var version) || version <= 0) throw new ValidationException(new Dictionary<string, string[]> { ["version"] = ["Version must be a positive number such as 1.0."] });
            row.Mode = input.Mode; row.Major = input.Major?.Trim() ?? ""; row.Version = version;
            row.Status = "DRAFT"; row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
            row.Gates = input.Gates.OrderBy(x => x.Sequence).Select(g => new ReviewGateDefinition
            {
                Code = g.Code.Trim(), Sequence = g.Sequence, Title = g.Title.Trim(), Objective = g.Objective?.Trim() ?? "",
                Required = g.Required, BlockNextGate = g.BlockNextGate, BlocksDefense = g.BlocksDefense,
                WindowStart = g.Start?.ToUniversalTime(), WindowEnd = g.End?.ToUniversalTime(),
                ReviewerPolicy = g.ReviewerPolicy.Trim(), ReviewScope = g.ReviewScope.Trim(),
                Requirements = (g.Requirements ?? []).Select(r => new ReviewGateRequirement { Code = r.Code.Trim(), Title = r.Title.Trim(), Description = r.Description?.Trim(), Type = r.Type.Trim(), Scope = r.Scope.Trim(), MajorId = r.MajorId, Verification = r.Verification.Trim(), Required = r.Required }).ToList()
            }).ToList();
            if (!id.HasValue) db.ReviewGateTemplates.Add(row);
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(Actor, "REVIEW_GATE_TEMPLATE_SAVED", "REVIEW_GATE_TEMPLATE", row.Id,
                new Dictionary<string, object?> { ["code"] = row.Code, ["version"] = row.Version }), token);
            var fresh = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleAsync(x => x.Id == row.Id, token);
            return Map(fresh);
        }, ct);

    public Task<ReviewGateTemplateDto> PublishTemplate(long id, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        var row = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("ReviewGateTemplate", id);
        if (row.Gates.Count == 0 || row.Gates.Any(g => g.Requirements.Count == 0)) throw new ValidationException(new Dictionary<string, string[]> { ["gates"] = ["Every gate must have at least one requirement."] });
        if (row.Status == "PUBLISHED") return Map(row);
        row.Status = "PUBLISHED"; row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_TEMPLATE_PUBLISHED", "REVIEW_GATE_TEMPLATE", id, new Dictionary<string, object?> { ["version"] = row.Version }), token);
        return Map(row);
    }, ct);

    public Task<StudentReviewTimelineDto> Timeline(long projectId, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, token)
            ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
        await RequireProjectRead(projectId, scope, token);
        var instances = await EnsureInstances(project, scope, token);
        return await TimelineDto(project, scope, instances, token);
    }, ct);

    public async Task<StudentReviewTimelineDto> MyTimeline(CancellationToken ct)
    {
        var projectId = await db.Projects.AsNoTracking()
            .Where(x => x.Team.TeamMembers.Any(m => m.UserId == Actor && m.LeftAt == null))
            .OrderByDescending(x => x.Status == "ACTIVE").ThenByDescending(x => x.UpdatedAt)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("ActiveProject", Actor);
        return await Timeline(projectId, ct);
    }

    public Task<StudentReviewTimelineDto> SubmitEvidence(long projectId, string gateCode, SubmitReviewGateEvidenceRequest input, CancellationToken ct) =>
        transactions.InTransactionAsync(async token =>
        {
            var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
            var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
            await RequireProjectLeader(projectId, token);
            var instances = await EnsureInstances(project, scope, token);
            var gate = instances.SingleOrDefault(x => x.Code == gateCode) ?? throw new NotFoundException("ProjectGate", 0);
            var requirementId = ParseId(input.RequirementId, "requirementId");
            var req = gate.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
            if (gate.ReviewEligibility == "BLOCKED") throw new ConflictException("The previous gate must be passed before submitting evidence.");
            if (string.IsNullOrWhiteSpace(input.ArtifactName) || input.ArtifactName.Length > 255) throw new ValidationException(new Dictionary<string, string[]> { ["artifactName"] = ["A valid artifact name is required."] });
            var latest = await db.GateEvidence.Where(x => x.RequirementId == req.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            if (input.ExpectedVersion != (latest?.Version ?? 0)) throw new ConflictException("The evidence changed; reload before submitting.");
            if (input.ConcurrencyToken is not null && (!Guid.TryParse(input.ConcurrencyToken, out var expected) || expected != gate.ConcurrencyToken)) throw new ConflictException("The gate changed; reload before submitting.");
            var evidence = new GateEvidence { ProjectId = projectId, RequirementId = req.Id, Version = (latest?.Version ?? 0) + 1, ArtifactName = input.ArtifactName.Trim(), Notes = input.Notes?.Trim(), SubmittedBy = Actor, SubmittedAt = Now, Status = "SUBMITTED" };
            db.GateEvidence.Add(evidence);
            await db.SaveChangesAsync(token);
            req.LatestEvidenceId = evidence.Id; req.VerificationStatus = "SUBMITTED"; gate.Status = "IN_REVIEW"; gate.ReviewEligibility = "READY"; gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(Actor, "REVIEW_GATE_EVIDENCE_SUBMITTED", "REVIEW_GATE", gate.Id, new Dictionary<string, object?> { ["requirementId"] = req.Id, ["version"] = evidence.Version }), token);
            var fresh = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
            return await TimelineDto(project, scope, fresh, token);
        }, ct);

    public async Task<StudentReviewTimelineDto> SubmitEvidenceFile(long projectId, string gateCode, SubmitReviewGateFileRequest input, UploadContent upload, CancellationToken ct)
    {
        var actor = Actor;
        var validated = await UploadValidator.ReadAsync(upload, ct);
        string? key = null;
        try
        {
            var result = await transactions.InTransactionAsync(async token =>
            {
                var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
                var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
                await RequireProjectLeader(projectId, token);
                var gate = (await EnsureInstances(project, scope, token)).SingleOrDefault(x => x.Code == gateCode) ?? throw new NotFoundException("ProjectGate", gateCode);
                if (gate.ReviewEligibility == "BLOCKED") throw new ConflictException("The previous gate must be passed before submitting evidence.");
                var requirementId = ParseId(input.RequirementId, "requirementId");
                var req = gate.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
                var latest = await db.GateEvidence.Where(x => x.RequirementId == req.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
                if (input.ExpectedVersion != (latest?.Version ?? 0)) throw new ConflictException("The evidence changed; reload before submitting.");
                var newKey = Guid.NewGuid().ToString("N");
                await storage.WriteAsync(newKey, new MemoryStream(validated.Bytes, writable: false), token);
                key = newKey;
                var evidence = new GateEvidence { ProjectId = projectId, RequirementId = req.Id, Version = (latest?.Version ?? 0) + 1,
                    ArtifactName = validated.FileName, ContentType = validated.ContentType, SizeBytes = validated.Bytes.LongLength,
                    Sha256 = validated.Sha256, StorageKey = newKey, Notes = input.Notes?.Trim(), SubmittedBy = actor, SubmittedAt = Now, Status = "SUBMITTED" };
                db.GateEvidence.Add(evidence); await db.SaveChangesAsync(token);
                req.LatestEvidenceId = evidence.Id; req.VerificationStatus = "SUBMITTED"; gate.Status = "IN_REVIEW"; gate.ReviewEligibility = "READY"; gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
                await audit.RecordAsync(new(actor, "REVIEW_GATE_EVIDENCE_FILE_SUBMITTED", "REVIEW_GATE", gate.Id,
                    new Dictionary<string, object?> { ["requirementId"] = req.Id, ["version"] = evidence.Version, ["sha256"] = evidence.Sha256 }), token);
                var current = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
                return await TimelineDto(project, scope, current, token);
            }, ct);
            return result!;
        }
        catch
        {
            if (key is not null) { try { await storage.DeleteAsync(key, CancellationToken.None); } catch { } }
            throw;
        }
    }

    public async Task<PagedResult<AssignedReviewItemDto>> AssignedReviews(int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize);
        var rows = await db.GateReviewerAssignments.AsNoTracking().Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence)
            .Where(x => x.ReviewerId == Actor && x.Status != "DECIDED").OrderBy(x => x.GateInstance.WindowEnd).ToArrayAsync(ct);
        var total = rows.LongLength;
        rows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var projectIds = rows.Select(x => x.GateInstance.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return new(rows.Select(x => AssignmentDto(x, projects[x.GateInstance.ProjectId])).ToArray(), page, pageSize, total);
    }

    public async Task<FileDownload> DownloadEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        await RequireProjectRead(projectId, scope, ct);
        var evidence = await db.GateEvidence.AsNoTracking().Include(x => x.Requirement).ThenInclude(x => x.GateInstance)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.Version == version && x.Requirement.GateInstance.Code == gateCode, ct)
            ?? throw new NotFoundException("ReviewGateEvidence", requirementId);
        if (!await IsAdmin(ct))
        {
            var memberOrSupervisor = await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.LeftAt == null, ct)
                || await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor, ct);
            var assigned = await db.GateReviewerAssignments.AnyAsync(x => x.GateInstanceId == evidence.Requirement.GateInstanceId && x.ReviewerId == Actor
                && x.Status != "REVOKED" && (x.Scope == "COMMON" || x.Scope == evidence.Requirement.Scope)
                && (x.Scope != "MAJOR_SPECIFIC" || x.MajorId == evidence.Requirement.MajorId), ct);
            if (!memberOrSupervisor && !assigned) throw new ForbiddenException("The evidence is outside the review assignment scope.");
        }
        try { return new(await storage.OpenReadAsync(evidence.StorageKey, ct), evidence.ContentType, evidence.ArtifactName); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException("ReviewGateEvidenceFile", evidence.Id); }
    }

    public Task<AssignedReviewItemDto> VerifyEvidence(long assignmentId, VerifyReviewGateEvidenceRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerId == Actor, token)
            ?? throw new NotFoundException("ReviewGateAssignment", assignmentId);
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        var requirementId = ParseId(input.RequirementId, "requirementId");
        var requirement = row.GateInstance.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
        if (row.Scope == "MAJOR_SPECIFIC" && (row.MajorId != requirement.MajorId || requirement.Scope != "MAJOR_SPECIFIC"))
            throw new ForbiddenException("The reviewer assignment does not cover this major requirement.");
        if (row.Scope == "INDIVIDUAL" && requirement.Scope != "INDIVIDUAL")
            throw new ForbiddenException("The reviewer assignment does not cover this individual requirement.");
        if (!await db.Users.AnyAsync(x => x.Id == Actor && x.Status == "ACTIVE", token)) throw new ForbiddenException("The reviewer account is inactive.");
        if (await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == row.GateInstance.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == Actor, token))
            throw new ForbiddenException("A project's active Primary or Mentor cannot review its own gate.");
        var evidence = requirement.Evidence.OrderByDescending(x => x.Version).FirstOrDefault() ?? throw new ConflictException("No evidence has been submitted.");
        if (input.Decision is not ("ACCEPTED" or "REJECTED")) throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Decision must be ACCEPTED or REJECTED."] });
        db.GateEvidenceVerifications.Add(new GateEvidenceVerification { EvidenceId = evidence.Id, AssignmentId = row.Id, Decision = input.Decision, Feedback = input.Feedback?.Trim(), ReviewerId = Actor, CreatedAt = Now });
        evidence.Status = input.Decision; requirement.VerificationStatus = input.Decision; requirement.ReviewerFeedback = input.Feedback?.Trim(); row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_EVIDENCE_VERIFIED", "REVIEW_GATE_ASSIGNMENT", row.Id, new Dictionary<string, object?> { ["requirementId"] = requirement.Id, ["decision"] = input.Decision }), token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == row.GateInstance.ProjectId, token); return AssignmentDto(row, project);
    }, ct);

    public Task<AssignedReviewItemDto> Decide(long assignmentId, SubmitReviewGateDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerId == Actor, token)
            ?? throw new NotFoundException("ReviewGateAssignment", assignmentId);
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (input.Decision is not ("PASSED" or "REVISION_REQUIRED") || string.IsNullOrWhiteSpace(input.Notes)) throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["A decision and notes are required."] });
        if (input.Decision == "PASSED" && row.GateInstance.Requirements.Any(x => x.Required && x.VerificationStatus != "ACCEPTED")) throw new ValidationException(new Dictionary<string, string[]> { ["gate"] = ["All mandatory evidence must be accepted before passing the gate."] });
        db.GateDecisions.Add(new GateDecision { AssignmentId = row.Id, Decision = input.Decision, Notes = input.Notes.Trim(), DecidedBy = Actor, DecidedAt = Now });
        row.LatestDecision = input.Decision; row.ReviewerNotes = input.Notes.Trim(); row.Status = "DECIDED"; row.ConcurrencyToken = Guid.NewGuid(); row.GateInstance.Status = input.Decision; row.GateInstance.ReviewEligibility = input.Decision == "PASSED" ? "READY" : "BLOCKED"; await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_DECISION_RECORDED", "REVIEW_GATE_ASSIGNMENT", row.Id, new Dictionary<string, object?> { ["decision"] = input.Decision }), token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == row.GateInstance.ProjectId, token); return AssignmentDto(row, project);
    }, ct);

    public Task<AssignedReviewItemDto> Assign(CreateReviewGateAssignmentRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        if (!long.TryParse(input.ProjectId, out var projectId) || projectId <= 0 || input.ReviewerId <= 0 || string.IsNullOrWhiteSpace(input.GateCode))
            throw new ValidationException(new Dictionary<string, string[]> { ["assignment"] = ["Project, gate and reviewer are required."] });
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        await RequireProjectRead(projectId, scope, token);
        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == input.GateCode, token)
            ?? throw new NotFoundException("ProjectGate", 0);
        if (input.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Unsupported review scope."] });
        if (input.Scope == "MAJOR_SPECIFIC" && (!input.MajorId.HasValue || !scope.MajorIds.Contains(input.MajorId.Value))) throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["Major is outside the frozen project scope."] });
        var reviewer = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.ReviewerId && x.Status == "ACTIVE"
            && x.UserRoleUsers.Any(r => r.Role.Code == "LECTURER" || r.Role.Code == "DEPARTMENT_STAFF"), token)
            ?? throw new NotFoundException("EligibleReviewer", input.ReviewerId);
        if (await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == reviewer.Id,
            token)) throw new ConflictException("A project's active Primary or Mentor cannot review its own gate.", "REVIEWER_CONFLICT");
        if (input.Scope == "MAJOR_SPECIFIC" && input.MajorId is not null)
        {
            var majorDepartment = await db.Majors.Where(m => m.Id == input.MajorId).Select(m => (long?)m.DepartmentId).SingleOrDefaultAsync(token);
            var sameDepartment = majorDepartment.HasValue && await db.Users.AnyAsync(u => u.Id == reviewer.Id && u.DepartmentId == majorDepartment, token);
            var assignedMajor = await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null
                && a.SupervisorProfile.UserId == reviewer.Id && a.MajorId == input.MajorId, token);
            if (!sameDepartment && !assignedMajor) throw new ForbiddenException("Reviewer is outside the requested major scope.");
        }
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.GateInstanceId == gate.Id && x.ReviewerId == reviewer.Id && x.Scope == input.Scope, token);
        if (row is null)
        {
            row = new GateReviewerAssignment { GateInstanceId = gate.Id, ReviewerId = reviewer.Id, Scope = input.Scope, MajorId = input.MajorId, CreatedAt = Now };
            db.GateReviewerAssignments.Add(row);
        }
        else { row.MajorId = input.MajorId; row.Status = "PENDING_REVIEW"; row.ConcurrencyToken = Guid.NewGuid(); }
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_ASSIGNMENT_CREATED", "REVIEW_GATE_ASSIGNMENT", row.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gate.Code, ["reviewerId"] = reviewer.Id, ["scope"] = input.Scope }), token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, token); return AssignmentDto(row, project);
    }, ct);

    public Task<StudentReviewTimelineDto> SubmitForReview(long projectId, string gateCode, SubmitReviewGateForReviewRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectLeader(projectId, token);
        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token) ?? throw new NotFoundException("ProjectGate", 0);
        if (input.ConcurrencyToken is not null) CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
        if (gate.ReviewEligibility == "BLOCKED") throw new ConflictException("The previous gate must be passed before submitting this gate.");
        if (gate.Requirements.Any(x => x.Required && x.Evidence.Count == 0)) throw new ValidationException(new Dictionary<string, string[]> { ["evidence"] = ["Every mandatory evidence item must be submitted before review."] });
        gate.Status = "IN_REVIEW"; gate.ReviewEligibility = "READY"; gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_SUBMITTED_FOR_REVIEW", "PROJECT_GATE", gate.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gateCode, ["notes"] = input.Notes }), token);
        var current = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
        return await TimelineDto(project, scope, current, token);
    }, ct);

    public Task<ProjectReviewMatrixRowDto> UpdateDecision(long projectId, string gateCode, UpdateReviewGateDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectRead(projectId, scope, token);
        if (input.NewStatus is not ("PASSED" or "REVISION_REQUIRED" or "IN_REVIEW")) throw new ValidationException(new Dictionary<string, string[]> { ["newStatus"] = ["Unsupported gate status."] });
        var gate = await db.ProjectGateInstances.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token) ?? throw new NotFoundException("ProjectGate", 0);
        if (input.ConcurrencyToken is not null) CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
        gate.Status = input.NewStatus; gate.ReviewEligibility = input.NewStatus == "REVISION_REQUIRED" ? "BLOCKED" : "READY"; gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_STATUS_UPDATED", "PROJECT_GATE", gate.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["status"] = input.NewStatus }), token);
        return await MatrixRow(projectId, token);
    }, ct);

    public Task<ProjectReviewMatrixRowDto> UpdateScope(long projectId, string scopeCode, UpdateReviewGateScopeRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectRead(projectId, scope, token);
        if (scope.MajorIds.Count < 2) throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Scope decisions require an interdisciplinary project."] });
        if (input.NewStatus is not ("PASSED" or "REVISION_REQUIRED" or "IN_REVIEW")) throw new ValidationException(new Dictionary<string, string[]> { ["newStatus"] = ["Unsupported scope status."] });
        var majorId = long.TryParse(scopeCode, out var parsed) ? parsed : await db.Majors.Where(x => x.Code == scopeCode || x.Name == scopeCode).Select(x => (long?)x.Id).SingleOrDefaultAsync(token);
        var normalized = scopeCode.Equals("COMMON", StringComparison.OrdinalIgnoreCase) ? "COMMON" : majorId.HasValue && scope.MajorIds.Contains(majorId.Value) ? majorId.Value.ToString() : throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Scope is outside the frozen project scope."] });
        var row = await db.ProjectGateScopeDecisions.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Scope == normalized, token);
        if (row is null) { row = new ProjectGateScopeDecision { ProjectId = projectId, Scope = normalized, MajorId = normalized == "COMMON" ? null : long.Parse(normalized), Status = input.NewStatus, DecidedBy = Actor, DecidedAt = Now }; db.ProjectGateScopeDecisions.Add(row); }
        else { if (input.ConcurrencyToken is not null) CheckToken(row.ConcurrencyToken, input.ConcurrencyToken); row.Status = input.NewStatus; row.DecidedBy = Actor; row.DecidedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); }
        await db.SaveChangesAsync(token); await audit.RecordAsync(new(Actor, "REVIEW_GATE_SCOPE_UPDATED", "PROJECT_GATE_SCOPE", row.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["scope"] = normalized, ["status"] = input.NewStatus }), token); return await MatrixRow(projectId, token);
    }, ct);

    public async Task<IReadOnlyList<ProjectReviewMatrixRowDto>> Matrix(string? mode, long? departmentId, CancellationToken ct)
    {
        await RequireStaff(ct);
        if (departmentId.HasValue && !await IsAdmin(ct))
        {
            var actorDepartment = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
            if (actorDepartment != departmentId) throw new ForbiddenException("The account is outside the requested department scope.");
        }
        var rows = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Project).ThenInclude(x => x.ProjectMajors).Where(x => (departmentId == null || x.Project.ProjectMajors.Any(pm => pm.Major.DepartmentId == departmentId.Value)) && (mode == null || mode == "ALL" || (mode == "INTERDISCIPLINARY" && x.Project.ProjectMajors.Count > 1) || (mode == "SINGLE_MAJOR" && x.Project.ProjectMajors.Count == 1))).GroupBy(x => x.ProjectId).ToArrayAsync(ct);
        var result = new List<ProjectReviewMatrixRowDto>(rows.Length);
        foreach (var group in rows) result.Add(await MatrixRow(group.Key, ct));
        return result;
    }

    public async Task<DefenseReadinessDto> DefenseReadiness(long projectId, CancellationToken ct)
    {
        var timeline = await Timeline(projectId, ct);
        var status = await db.Projects.AsNoTracking().Where(x => x.Id == projectId).Select(x => x.Status).SingleAsync(ct);
        var conditions = timeline.Gates.Where(x => x.IsRequired).Select(x => new DefenseConditionDto(x.Code, x.Title, x.Status == "PASSED", x.Status == "PASSED" ? "Đã đạt" : "Chưa đạt Review Gate", true)).ToList();
        conditions.Add(new("FINAL_PACKAGE", "Hồ sơ bàn giao cuối kỳ", status is "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED",
            status is "FINAL_SUBMISSION" or "COMPLETED" or "ARCHIVED" ? "Đã khóa hồ sơ bàn giao" : "Dự án chưa ở giai đoạn bàn giao cuối kỳ", true));
        if (timeline.ProjectMode == "INTERDISCIPLINARY")
        {
            var majorCount = await db.ProjectMajors.CountAsync(x => x.ProjectId == projectId, ct);
            var decided = await db.ProjectGateScopeDecisions.CountAsync(x => x.ProjectId == projectId && x.Status == "PASSED", ct);
            conditions.Add(new("INTERDISCIPLINARY_SCOPE", "Xác nhận phạm vi liên ngành", decided >= majorCount + 1,
                decided >= majorCount + 1 ? "Các khoa đã xác nhận phạm vi" : "Chưa đủ quyết định phạm vi của COMMON và các ngành", true));
        }
        return new(projectId.ToString(), timeline.ProjectName, conditions.All(x => x.Satisfied == true), conditions.Count(x => x.Satisfied != true), conditions);
    }

    public async Task<DefenseReadinessDto> DefenseReadinessByKey(string projectKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectKey)) throw new ValidationException(new Dictionary<string, string[]> { ["projectId"] = ["Project id or code is required."] });
        var id = long.TryParse(projectKey, out var numeric)
            ? (long?)numeric
            : await db.Projects.AsNoTracking().Where(x => x.Code == projectKey).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct);
        if (!id.HasValue) throw new NotFoundException("Project", projectKey);
        return await DefenseReadiness(id.Value, ct);
    }

    private async Task<ProjectGateInstance[]> EnsureInstances(AIPMS.Infrastructure.Persistence.Generated.Models.Project project, ProjectAcademicScope scope, CancellationToken ct)
    {
        var current = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == project.Id).OrderBy(x => x.Sequence).ToArrayAsync(ct);
        if (current.Length > 0) return current;
        var mode = scope.MajorIds.Count > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var template = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).Where(x => x.Status == "PUBLISHED" && x.Mode == mode).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (template is null) return [];
        var created = template.Gates.OrderBy(x => x.Sequence).Select(g => new ProjectGateInstance { ProjectId = project.Id, TemplateId = template.Id, TemplateVersion = template.Version, Code = g.Code, Sequence = g.Sequence, Title = g.Title, Objective = g.Objective, Status = g.Sequence == 1 ? "PLANNED" : "LOCKED", ReviewEligibility = g.Sequence == 1 ? "READY" : "BLOCKED", Required = g.Required, WindowStart = g.WindowStart, WindowEnd = g.WindowEnd, CreatedAt = Now, Requirements = g.Requirements.Select(r => new ProjectGateRequirement { Code = r.Code, Title = r.Title, Type = r.Type, Scope = r.Scope, MajorId = r.MajorId, Required = r.Required }).ToList() }).ToArray();
        db.ProjectGateInstances.AddRange(created); await db.SaveChangesAsync(ct); return created;
    }

    private async Task<StudentReviewTimelineDto> TimelineDto(AIPMS.Infrastructure.Persistence.Generated.Models.Project project, ProjectAcademicScope scope, ProjectGateInstance[] gates, CancellationToken ct)
    {
        var current = gates.FirstOrDefault(x => x.Status != "PASSED") ?? gates.LastOrDefault();
        var leader = await db.TeamMembers.AnyAsync(x => x.TeamId == project.TeamId && x.UserId == Actor && x.IsLeader && x.LeftAt == null, ct);
        var mode = scope.MajorIds.Count > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var majorNames = await db.Majors.AsNoTracking().Where(x => scope.MajorIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Name).ToArrayAsync(ct);
        var policyVersion = gates.FirstOrDefault()?.TemplateVersion.ToString("0.##") ?? "UNKNOWN";
        return new(project.Id.ToString(), project.Code, project.Title, mode, string.Join(" + ", majorNames), policyVersion, current?.Code ?? "", gates.Select(g => new StudentGateInstanceDto(g.Id.ToString(), g.Code, g.Title, g.Objective, g.Status, g.ReviewEligibility, g.Required, g.WindowStart, g.WindowEnd, Blocking(g), g.Requirements.Select(r => new StudentGateRequirementDto(r.Id.ToString(), r.Code, r.Title, r.Type, r.Scope, r.Required, r.VerificationStatus, Artifact(r, g.Code), r.ReviewerFeedback)).ToArray())).ToArray(), new(leader && current is not null && current.ReviewEligibility == "READY", leader && current is not null && current.Status == "REVISION_REQUIRED", leader ? [] : ["Chỉ trưởng nhóm mới được nộp minh chứng."]));
    }

    private static IReadOnlyList<ReviewGateBlockingReasonDto> Blocking(ProjectGateInstance g) => g.ReviewEligibility == "BLOCKED" ? [new("PREVIOUS_GATE_REQUIRED", null, "Cổng trước chưa đạt.")] : g.Requirements.Where(x => x.Required && x.VerificationStatus != "ACCEPTED").Select(x => new ReviewGateBlockingReasonDto("EVIDENCE_REQUIRED", x.Code, "Minh chứng bắt buộc chưa được chấp thuận.")).ToArray();
    private static ReviewGateArtifactDto? Artifact(ProjectGateRequirement r, string gateCode)
    {
        var latest = r.Evidence.OrderByDescending(x => x.Version).FirstOrDefault();
        return latest is null ? null : new(latest.Version, latest.ArtifactName, latest.SubmittedAt, latest.SubmittedBy.ToString(), latest.Notes,
            latest.ContentType, latest.SizeBytes, latest.Sha256,
            $"/api/v1/projects/{latest.ProjectId}/review-gates/{gateCode}/requirements/{latest.RequirementId}/evidence/{latest.Version}/download");
    }
    private static void CheckToken(Guid current, string input) { if (!Guid.TryParse(input, out var value) || value != current) throw new ConflictException("The review gate changed; reload before retrying."); }
    private static long ParseId(string value, string field) => long.TryParse(value, out var id) && id > 0 ? id : throw new ValidationException(new Dictionary<string, string[]> { [field] = ["A positive numeric id is required."] });

    private async Task RequireProjectRead(long projectId, ProjectAcademicScope scope, CancellationToken ct)
    {
        if (await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.LeftAt == null, ct)) return;
        if (await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor, ct)) return;
        var actorDept = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
        if (await IsAdmin(ct)) return;
        if (await IsStaff(ct) && actorDept.HasValue && scope.DepartmentIds.Contains(actorDept.Value)) return;
        throw new ForbiddenException("The account is outside the project academic scope.");
    }
    private async Task RequireProjectLeader(long projectId, CancellationToken ct)
    { if (!await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.IsLeader && x.LeftAt == null, ct)) throw new ForbiddenException("Only the active project leader may submit evidence."); }

    private static ReviewGateTemplateDto Map(ReviewGateTemplate x) => new(x.Id.ToString(), x.ProjectPeriodId, x.Code, x.Name, x.Mode, x.Major, x.Status, x.Version.ToString("0.##"), x.ConcurrencyToken.ToString("N"), x.Gates.Count, x.Gates.OrderBy(g => g.Sequence).Select(g => new ReviewGateDefinitionDto(g.Id.ToString(), g.Code, g.Sequence, g.Title, g.Objective, g.Required, g.BlockNextGate, g.BlocksDefense, g.WindowStart, g.WindowEnd, g.WindowStart, g.WindowEnd, g.ReviewerPolicy, g.ReviewScope, g.Requirements.OrderBy(r => r.Id).Select(r => new ReviewGateRequirementDto(r.Id.ToString(), r.Code, r.Title, r.Description, r.Type, r.Scope, r.MajorId, r.Verification, r.Required)).ToArray())).ToArray());
    private static AssignedReviewItemDto AssignmentDto(GateReviewerAssignment x, AIPMS.Infrastructure.Persistence.Generated.Models.Project project) => new(x.Id.ToString(), project.Id.ToString(), project.Code, project.Title, x.GateInstance.Code, x.GateInstance.Title, x.Scope, x.GateInstance.WindowEnd, x.Status, x.LatestDecision, x.GateInstance.Requirements.Select(r => new StudentGateRequirementDto(r.Id.ToString(), r.Code, r.Title, r.Type, r.Scope, r.Required, r.VerificationStatus, Artifact(r, x.GateInstance.Code), r.ReviewerFeedback)).ToArray(), x.ReviewerNotes, x.ConcurrencyToken.ToString("N"));
    private async Task<ProjectReviewMatrixRowDto> MatrixRow(long projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Include(x => x.ProjectMajors).ThenInclude(x => x.Major).ThenInclude(x => x.Department).SingleAsync(x => x.Id == projectId, ct);
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(ct);
        var scopes = await db.ProjectGateScopeDecisions.AsNoTracking().Where(x => x.ProjectId == projectId).ToDictionaryAsync(x => x.Scope, x => x.Status, ct);
        var majors = project.ProjectMajors.Select(x => x.Major.Name).ToArray();
        var taskCount = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == projectId, ct);
        var doneCount = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == projectId && (x.Status == "DONE" || x.Status == "COMPLETED"), ct);
        var taskProgress = taskCount == 0 ? 0m : Math.Round(doneCount * 100m / taskCount, 2);
        return new(project.Id.ToString(), project.Title, majors.Length > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR", string.Join(" + ", majors), string.Join(" + ", project.ProjectMajors.Select(x => x.Major.Department.Name).Distinct()), taskProgress, gates.ToDictionary(x => x.Code, x => x.Status), new Dictionary<string, IReadOnlyList<string>>(), scopes);
    }
}
