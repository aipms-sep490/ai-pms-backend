using System.Security.Cryptography;
using System.Text;
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
using AIPMS.Application.Features.Files;
using AIPMS.Application.Features.Files.Abstractions;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Domain.Exceptions;
using AIPMS.Infrastructure.Persistence.Generated;
using UserModel = AIPMS.Infrastructure.Persistence.Generated.Models.User;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class ReviewGateService(AipmsDbContext db, ICurrentUser current, IProjectRepository transactions,
    IAuditTrail audit, TimeProvider clock, IFileStorage storage, ColdSubmissionService cold,
    IFileSecurityScanner scanner, ISignedPreviewTokenService previewTokens, IOfficePreviewService officePreview,
    AIPMS.Application.Features.Semesters.Abstractions.IWorkingDayCalculator? workingDayCalculator = null) : IReviewGateService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task NotifyGateChanged(long projectId, long gateId, string gateCode, string eventName, CancellationToken ct,
        bool enqueueEmail = false, bool includeDepartmentStaff = false)
    {
        var recipientsQuery = db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE" && u.Id != Actor &&
                (u.TeamMembers.Any(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.LeftAt == null) ||
                 db.SupervisorAssignments.Any(a => a.ProjectId == projectId && a.EndedAt == null && a.SupervisorProfile.UserId == u.Id) ||
                 db.GateReviewerAssignments.Any(a => a.GateInstanceId == gateId && a.ReviewerId == u.Id && a.Status != "REVOKED") ||
                 (includeDepartmentStaff
                    && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")
                    && db.ProjectMajors.Any(pm => pm.ProjectId == projectId && pm.Major.IsActive && pm.Major.DepartmentId == u.DepartmentId))));
        var recipients = await recipientsQuery.Select(u => u.Id).Distinct().ToArrayAsync(ct);
        if (recipients.Length == 0) return;
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new { p.Code, p.Title }).FirstOrDefaultAsync(ct);
        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == Actor)
            .Select(u => u.FullName).FirstOrDefaultAsync(ct);
        var projectCode = project?.Code;
        var projectFull = project is null ? null : string.IsNullOrWhiteSpace(project.Title) ? project.Code : $"{project.Code} - {project.Title}";
        var rawTitle = string.IsNullOrWhiteSpace(projectCode)
            ? $"Cập nhật cổng kiểm duyệt {gateCode}"
            : $"Cập nhật cổng kiểm duyệt {gateCode} - Đồ án {projectCode}";
        var title = rawTitle.Length <= 255 ? rawTitle : rawTitle[..255];
        var prefix = projectFull is null ? "" : $"Đồ án {projectFull}: ";
        var actorSuffix = string.IsNullOrWhiteSpace(actorName) ? "" : $" (Thực hiện bởi {actorName})";
        var content = $"{prefix}{eventName}{actorSuffix}";
        var now = Now;
        var notification = new AIPMS.Infrastructure.Persistence.Generated.Models.Notification
        {
            CreatedBy = Actor,
            NotificationType = "REVIEW_GATE_UPDATED",
            Title = title,
            Content = content,
            RelatedEntityType = "PROJECT_GATE",
            RelatedEntityId = gateId,
            CreatedAt = now,
            UpdatedAt = now,
            NotificationRecipients = recipients.Select(id => new AIPMS.Infrastructure.Persistence.Generated.Models.NotificationRecipient
            {
                UserId = id, CreatedAt = now, UpdatedAt = now, DeliveredAt = now
            }).ToArray()
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
        if (enqueueEmail)
        {
            db.AddRange(notification.NotificationRecipients.Select(r => new AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery
            {
                NotificationRecipientId = r.Id,
                NextAttemptAt = now
            }));
            await db.SaveChangesAsync(ct);
        }
    }

    private static string GateStatusLabel(string status) => status switch
    {
        "PASSED" => "Đạt",
        "REVISION_REQUIRED" => "Cần chỉnh sửa",
        "IN_REVIEW" => "Đang kiểm duyệt",
        "FAILED" => "Không đạt",
        _ => "Đã cập nhật"
    };

    private async Task<bool> IsStaff(CancellationToken ct) => await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE" &&
        u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN" || r.Role.Code == "DEPARTMENT_STAFF"), ct);
    private async Task<bool> IsAdmin(CancellationToken ct) => await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE" &&
        u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), ct);

    private async Task RequireStaff(CancellationToken ct)
    { if (!await IsStaff(ct)) throw new ForbiddenException("Academic staff access is required."); }

    private async Task<UserModel> RequireTemplateStaffUser(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.UserRoleUsers).ThenInclude(r => r.Role)
            .Include(u => u.Department).ThenInclude(d => d!.Organization)
            .FirstOrDefaultAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct);

        if (user == null)
            throw new ForbiddenException("An active account is required.");

        var roles = user.UserRoleUsers.Select(r => r.Role.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool isAdmin = roles.Contains("ADMIN");
        bool isStaff = roles.Contains("DEPARTMENT_STAFF");

        if (isAdmin && !isStaff)
            throw new ForbiddenException("Administrative accounts without academic department staff authority cannot manage review gate templates.", "TEMPLATE_AUTHORITY_ADMIN_NOT_ACADEMIC");

        if (!isStaff)
            throw new ForbiddenException("Academic staff access is required.");

        if (!user.DepartmentId.HasValue)
            throw new ForbiddenException("The account has no academic department scope.", "TEMPLATE_SCOPE_FORBIDDEN");

        return user;
    }

    private async Task RequireTemplateAuthority(long id, CancellationToken ct)
    {
        var user = await RequireTemplateStaffUser(ct);
        var row = await db.ReviewGateTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("ReviewGateTemplate", id);
        await ValidateTemplateScope(user, row.Mode, row.Major, row.ProjectPeriodId, ct);
    }

    private async Task RequireTemplateAuthority(SaveReviewGateTemplateRequest input, CancellationToken ct) =>
        await RequireTemplateAuthority(input.Mode, input.Major, input.ProjectPeriodId, ct);

    private async Task RequireTemplateAuthority(string mode, string? major, long? projectPeriodId, CancellationToken ct)
    {
        var user = await RequireTemplateStaffUser(ct);
        await ValidateTemplateScope(user, mode, major, projectPeriodId, ct);
    }

    private async Task ValidateTemplateScope(UserModel user, string mode, string? major, long? projectPeriodId, CancellationToken ct)
    {
        var actorDepartmentId = user.DepartmentId!.Value;

        if (!string.IsNullOrWhiteSpace(major))
        {
            var majorNormalized = major.Trim();
            var majorTokens = majorNormalized.Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (majorTokens.Length == 1 && !majorNormalized.Equals("INTERDISCIPLINARY", StringComparison.OrdinalIgnoreCase))
            {
                var majorRow = await db.Majors.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Code == majorNormalized || m.Name == majorNormalized || m.Id.ToString() == majorNormalized, ct);
                if (majorRow != null && majorRow.DepartmentId != actorDepartmentId)
                {
                    throw new ForbiddenException($"The account is outside the requested major scope '{majorNormalized}'.", "TEMPLATE_SCOPE_FORBIDDEN");
                }
            }
        }

        if (projectPeriodId.HasValue)
        {
            var period = await db.ProjectPeriods.AsNoTracking()
                .Include(p => p.AcademicSemester)
                .Include(p => p.Rubric)
                .FirstOrDefaultAsync(p => p.Id == projectPeriodId.Value, ct);

            if (period != null)
            {
                if (period.AcademicSemester != null && user.Department != null && period.AcademicSemester.OrganizationId != user.Department.OrganizationId)
                {
                    throw new ForbiddenException($"The account is outside the requested period organization scope '{projectPeriodId.Value}'.", "TEMPLATE_SCOPE_FORBIDDEN");
                }

                if (mode == "INTERDISCIPLINARY")
                {
                    var snapshotLead = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
                        .Where(s => s.ProjectPeriodId == projectPeriodId.Value)
                        .Select(s => (long?)s.LeadDepartmentId).FirstOrDefaultAsync(ct);

                    var topicLead = await db.Set<ProjectTopic>().AsNoTracking()
                        .Where(t => t.ProjectPeriodId == projectPeriodId.Value)
                        .Select(t => (long?)t.LeadDepartmentId).FirstOrDefaultAsync(ct);

                    var rubricDept = period.Rubric?.DepartmentId;

                    var leadDeptId = snapshotLead ?? topicLead ?? rubricDept;
                    if (leadDeptId.HasValue && leadDeptId.Value != actorDepartmentId)
                    {
                        throw new ForbiddenException($"Only the lead department may manage interdisciplinary templates for period '{projectPeriodId.Value}'.", "TEMPLATE_SCOPE_FORBIDDEN");
                    }
                }
                else
                {
                    if (period.Rubric?.DepartmentId != null && period.Rubric.DepartmentId.Value != actorDepartmentId)
                    {
                        throw new ForbiddenException($"The account is outside the requested period department scope '{projectPeriodId.Value}'.", "TEMPLATE_SCOPE_FORBIDDEN");
                    }
                }
            }
        }
    }

    private static void Page(int page, int size)
    { if (page is < 1 or > 100000 || size is < 1 or > 100) throw new ValidationException(new Dictionary<string, string[]> { ["page"] = ["Invalid bounded pagination."] }); }

    public async Task<IReadOnlyList<ReviewGateTemplateDto>> Templates(CancellationToken ct)
    {
        var user = await RequireTemplateStaffUser(ct);
        var rows = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements)
            .OrderByDescending(x => x.UpdatedAt).ToArrayAsync(ct);
        var visible = new List<ReviewGateTemplateDto>();
        foreach (var row in rows)
        {
            try
            {
                await ValidateTemplateScope(user, row.Mode, row.Major, row.ProjectPeriodId, ct);
                visible.Add(Map(row));
            }
            catch (ForbiddenException) { /* A template outside this department is omitted from the list. */ }
        }
        return visible;
    }

    public async Task<ReviewGateTemplateDto?> Template(long id, CancellationToken ct)
    {
        var user = await RequireTemplateStaffUser(ct);
        var row = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is not null) await ValidateTemplateScope(user, row.Mode, row.Major, row.ProjectPeriodId, ct);
        return row is null ? null : Map(row);
    }

    public Task<ReviewGateTemplateDto> SaveTemplate(SaveReviewGateTemplateRequest input, long? id, CancellationToken ct) =>
        transactions.InTransactionAsync(async token =>
        {
            await RequireTemplateAuthority(input, token);
            if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name) || input.Gates is null || input.Gates.Count == 0)
                throw new ValidationException(new Dictionary<string, string[]> { ["template"] = ["Code, name and at least one gate are required."] });
            if (input.Mode is not ("SINGLE_MAJOR" or "INTERDISCIPLINARY")) throw new ValidationException(new Dictionary<string, string[]> { ["mode"] = ["Unsupported project mode."] });
            if (input.Gates.Any(g => g.Sequence < 1) || input.Gates.GroupBy(g => g.Sequence).Any(g => g.Count() > 1) || input.Gates.GroupBy(g => g.Code.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new ValidationException(new Dictionary<string, string[]> { ["gates"] = ["Gate codes and sequence numbers must be unique positive values."] });
            var weights = input.Gates.Select(g => g.Weight).ToArray();
            if (weights.Any(x => x is < 0 or > 100) || weights.Any(x => x.HasValue) && (weights.Any(x => !x.HasValue) || weights.Sum(x => x ?? 0) != 100m))
                throw new ValidationException(new Dictionary<string, string[]> { ["weights"] = ["Either leave all gate weights unset, or provide non-negative weights totaling 100."] });
            foreach (var requirement in input.Gates.SelectMany(g => g.Requirements ?? []))
            {
                var normalizedScope = requirement.Scope.Trim().ToUpperInvariant();
                var isEachStudent = string.Equals(requirement.TargetCardinality, "EACH_STUDENT", StringComparison.OrdinalIgnoreCase);
                var validTarget = normalizedScope is "COMMON" or "INTEGRATION" && requirement.MajorId is null && requirement.TargetStudentId is null
                    || normalizedScope == "MAJOR_SPECIFIC" && requirement.MajorId is > 0 && requirement.TargetStudentId is null
                    || normalizedScope == "INDIVIDUAL" && ((requirement.MajorId is > 0 && requirement.TargetStudentId is > 0) || isEachStudent);
                if (normalizedScope is not ("COMMON" or "INTEGRATION" or "MAJOR_SPECIFIC" or "INDIVIDUAL") || !validTarget)
                    throw new ValidationException(new Dictionary<string, string[]> { ["requirements"] = ["Each requirement must have a valid COMMON, INTEGRATION, MAJOR_SPECIFIC, or INDIVIDUAL (or EACH_STUDENT) target."] });
            }
            ReviewGateTemplate row;
            if (id.HasValue)
            {
                row = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == id, token)
                    ?? throw new NotFoundException("ReviewGateTemplate", id.Value);
                await RequireTemplateAuthority(row.Mode, row.Major, row.ProjectPeriodId, token);
                if (row.Status == "PUBLISHED") throw new ConflictException("Published templates are immutable; create a new version.");
                CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
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
                Weight = g.Weight,
                WindowStart = g.Start?.ToUniversalTime(), WindowEnd = g.End?.ToUniversalTime(),
                ReviewerPolicy = g.ReviewerPolicy.Trim(), ReviewScope = g.ReviewScope.Trim(),
                Requirements = (g.Requirements ?? []).Select(r => new ReviewGateRequirement
                {
                    Code = r.Code.Trim(), Title = r.Title.Trim(), Description = r.Description?.Trim(), Type = r.Type.Trim(),
                    Scope = r.Scope.Trim().ToUpperInvariant(), MajorId = r.MajorId, TargetStudentId = r.TargetStudentId,
                    Verification = r.Verification.Trim(), Required = r.Required,
                    TargetCardinality = string.IsNullOrWhiteSpace(r.TargetCardinality) ? "PROJECT" : r.TargetCardinality.Trim().ToUpperInvariant()
                }).ToList()
            }).ToList();
            if (!id.HasValue) db.ReviewGateTemplates.Add(row);
            await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(Actor, "REVIEW_GATE_TEMPLATE_SAVED", "REVIEW_GATE_TEMPLATE", row.Id,
                new Dictionary<string, object?> { ["code"] = row.Code, ["version"] = row.Version }), token);
            var fresh = await db.ReviewGateTemplates.AsNoTracking().Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleAsync(x => x.Id == row.Id, token);
            return Map(fresh);
        }, ct);

    public Task<ReviewGateTemplateDto> PublishTemplate(long id, PublishReviewGateTemplateRequest? input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireTemplateAuthority(id, token);
        var row = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("ReviewGateTemplate", id);
        if (input is null || string.IsNullOrWhiteSpace(input.ConcurrencyToken))
            throw new ValidationException(new Dictionary<string, string[]> { ["concurrencyToken"] = ["A concurrency token is required to publish a template."] });
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Gates.Count == 0 || row.Gates.Any(g => g.Requirements.Count == 0)) throw new ValidationException(new Dictionary<string, string[]> { ["gates"] = ["Every gate must have at least one requirement."] });
        if (row.Status == "PUBLISHED") return Map(row);
        row.Status = "PUBLISHED"; row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_TEMPLATE_PUBLISHED", "REVIEW_GATE_TEMPLATE", id, new Dictionary<string, object?> { ["version"] = row.Version }), token);
        return Map(row);
    }, ct);

    public Task DeleteTemplate(long id, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireTemplateAuthority(id, token);
        var row = await db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements).SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("ReviewGateTemplate", id);
        if (!string.Equals(row.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Chỉ được xóa mẫu tiến trình ở trạng thái Bản nháp (DRAFT). Mẫu đã công bố không thể xóa.");
        if (await db.ProjectGateInstances.AsNoTracking().AnyAsync(x => x.TemplateId == id, token))
            throw new ConflictException("Mẫu tiến trình này đang được sử dụng bởi đồ án nên không thể xóa.");

        var code = row.Code;
        var name = row.Name;
        var version = row.Version;
        db.ReviewGateRequirements.RemoveRange(row.Gates.SelectMany(x => x.Requirements));
        db.ReviewGateDefinitions.RemoveRange(row.Gates);
        db.ReviewGateTemplates.Remove(row);
        await db.SaveChangesAsync(token);

        await audit.RecordAsync(new(Actor, "REVIEW_GATE_TEMPLATE_DELETED", "REVIEW_GATE_TEMPLATE", id,
            new Dictionary<string, object?> { ["code"] = code, ["name"] = name, ["version"] = version }), token);
        return true;
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
            if (project.Status != "ACTIVE")
                throw new ConflictException("Review gate evidence can only be uploaded while the project is ACTIVE (D18).", WorkflowErrorCodes.ProjectNotActive);
            var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
            var instances = await EnsureInstances(project, scope, token);
            var gate = instances.SingleOrDefault(x => x.Code == gateCode) ?? throw new NotFoundException("ProjectGate", 0);
            if (gate.Status is "PASSED" or "FAILED" || gate.DecisionSource == "OVERRIDE")
                throw new ConflictException("Passed, failed, or overridden review gates are locked. Lead department staff must reopen the gate first.", WorkflowErrorCodes.GateImmutableLocked);
            var now = Now;
            var effectiveDue = gate.Status == "REVISION_REQUIRED" ? (gate.RevisionDueAt ?? gate.WindowEnd) : (gate.SubmissionDueAt ?? gate.WindowEnd);
            if ((gate.WindowStart.HasValue && now < gate.WindowStart.Value) || (effectiveDue.HasValue && now > effectiveDue.Value))
                throw new ConflictException("The review gate submission window is closed.", WorkflowErrorCodes.GateWindowClosed);
            var requirementId = ParseId(input.RequirementId, "requirementId");
            var req = gate.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
            await RequireEvidenceDraftUploader(projectId, req, token);
            var externalLink = string.Equals(req.Type, "EXTERNAL_LINK", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(req.Type, "FILE", StringComparison.OrdinalIgnoreCase))
                throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["FILE requirements must use multipart upload."] });
            if (externalLink)
            {
                if (!Uri.TryCreate(input.LinkUrl, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(link.Host))
                    throw new ValidationException(new Dictionary<string, string[]> { ["linkUrl"] = ["A valid https URL is required for an external-link requirement."] });
            }
            if (gate.ReviewEligibility == "BLOCKED" && gate.Status != "REVISION_REQUIRED") throw new ConflictException("The previous gate must be passed before submitting evidence.");
            if (!externalLink && (string.IsNullOrWhiteSpace(input.ArtifactName) || input.ArtifactName.Length > 255)) throw new ValidationException(new Dictionary<string, string[]> { ["artifactName"] = ["A valid artifact name is required."] });
            var latest = await db.GateEvidence.Where(x => x.RequirementId == req.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
            if (input.ExpectedVersion != (latest?.Version ?? 0)) throw new ConflictException("The evidence changed; reload before submitting.");
            CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
            var evidence = new GateEvidence { ProjectId = projectId, RequirementId = req.Id, Version = (latest?.Version ?? 0) + 1,
                ArtifactName = externalLink ? input.LinkUrl!.Trim() : input.ArtifactName.Trim(), LinkUrl = externalLink ? input.LinkUrl!.Trim() : null,
                ContentType = externalLink ? "text/uri-list" : "application/octet-stream", Notes = input.Notes?.Trim(), SubmittedBy = Actor, SubmittedAt = now, Status = "SUBMITTED" };
            db.GateEvidence.Add(evidence);
            await db.SaveChangesAsync(token);
            // TK-W2-07: Draft evidence upload saves the draft only; SubmitForReview freezes the package and transitions to IN_REVIEW.
            req.LatestEvidenceId = evidence.Id; req.VerificationStatus = "SUBMITTED"; gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
            await audit.RecordAsync(new(Actor, "REVIEW_GATE_EVIDENCE_SUBMITTED", "REVIEW_GATE", gate.Id, new Dictionary<string, object?> { ["requirementId"] = req.Id, ["version"] = evidence.Version }), token);
            var fresh = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
            return await TimelineDto(project, scope, fresh, token);
        }, ct);

    public async Task<StudentReviewTimelineDto> SubmitEvidenceFile(long projectId, string gateCode, SubmitReviewGateFileRequest input, UploadContent upload, CancellationToken ct)
    {
        var actor = Actor;
        var validated = await UploadValidator.ReadAsync(upload, ct, allowArchiveVideoTier: true, allowQuarantineFallback: true);
        FileScanResult scan;
        using (var scanStream = new MemoryStream(validated.Bytes, writable: false))
        {
            scan = await scanner.ScanAsync(scanStream, validated.FileName, validated.ContentType, ct);
        }
        string? key = null;
        try
        {
            var result = await transactions.InTransactionAsync(async token =>
            {
                var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
                if (project.Status != "ACTIVE")
                    throw new ConflictException("Review gate evidence files can only be uploaded while the project is ACTIVE (D18).", WorkflowErrorCodes.ProjectNotActive);
                var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token);
                var gate = (await EnsureInstances(project, scope, token)).SingleOrDefault(x => x.Code == gateCode) ?? throw new NotFoundException("ProjectGate", gateCode);
                if (gate.Status is "PASSED" or "FAILED" || gate.DecisionSource == "OVERRIDE")
                    throw new ConflictException("Passed, failed, or overridden review gates are locked. Lead department staff must reopen the gate first.", WorkflowErrorCodes.GateImmutableLocked);
                if (gate.ReviewEligibility == "BLOCKED" && gate.Status != "REVISION_REQUIRED") throw new ConflictException("The previous gate must be passed before submitting evidence.");
                var now = Now;
                var effectiveDue = gate.Status == "REVISION_REQUIRED" ? (gate.RevisionDueAt ?? gate.WindowEnd) : (gate.SubmissionDueAt ?? gate.WindowEnd);
                if ((gate.WindowStart.HasValue && now < gate.WindowStart.Value) || (effectiveDue.HasValue && now > effectiveDue.Value))
                    throw new ConflictException("The review gate submission window is closed.", WorkflowErrorCodes.GateWindowClosed);
                var requirementId = ParseId(input.RequirementId, "requirementId");
                var req = gate.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
                await RequireEvidenceDraftUploader(projectId, req, token);
                if (string.Equals(req.Type, "EXTERNAL_LINK", StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["EXTERNAL_LINK requirements are not supported by this contract."] });
                CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
                var latest = await db.GateEvidence.Where(x => x.RequirementId == req.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
                if (input.ExpectedVersion != (latest?.Version ?? 0)) throw new ConflictException("The evidence changed; reload before submitting.");

                var newKey = "";
                if (scan.IsSafe)
                {
                    newKey = Guid.NewGuid().ToString("N");
                    await storage.WriteAsync(newKey, new MemoryStream(validated.Bytes, writable: false), token);
                    key = newKey;
                }
                var evidence = new GateEvidence
                {
                    ProjectId = projectId, RequirementId = req.Id, Version = (latest?.Version ?? 0) + 1,
                    ArtifactName = validated.FileName, ContentType = validated.ContentType, SizeBytes = validated.Bytes.LongLength,
                    Sha256 = validated.Sha256, StorageKey = newKey, Notes = input.Notes?.Trim(), SubmittedBy = actor, SubmittedAt = now,
                    Status = scan.IsSafe ? "SUBMITTED" : FileScanStatuses.Quarantined,
                    ScanStatus = scan.Status, ScanProvider = scan.Provider, ScannedAt = now, ScanDetails = scan.Details
                };
                db.GateEvidence.Add(evidence); await db.SaveChangesAsync(token);
                if (scan.IsSafe)
                {
                    // TK-W2-07: Draft upload never resets override or prematurely transitions the gate to IN_REVIEW.
                    req.LatestEvidenceId = evidence.Id; req.VerificationStatus = "SUBMITTED";
                    gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
                    // TK-W2-03: If a gate-linked core task exists for this requirement, auto-record it to the project evidence ledger.
                    var linkedTaskId = await db.Tasks.AsNoTracking()
                        .Where(t => t.GateRequirementId == req.Id)
                        .Select(t => (long?)t.Id)
                        .FirstOrDefaultAsync(token);
                    if (linkedTaskId.HasValue)
                    {
                        await ProjectEvidenceAutoRecorder.TryRecordAsync(
                            db, projectId, req.MajorId, "TASK", linkedTaskId.Value,
                            $"Tự động ghi nhận khi tải tệp minh chứng cổng {gate.Code}: {validated.FileName}",
                            actor, now, token);
                    }
                    await audit.RecordAsync(new(actor, "REVIEW_GATE_EVIDENCE_FILE_SUBMITTED", "REVIEW_GATE", gate.Id,
                        new Dictionary<string, object?> { ["requirementId"] = req.Id, ["version"] = evidence.Version, ["sha256"] = evidence.Sha256, ["scanStatus"] = evidence.ScanStatus }), token);
                }
                else
                {
                    req.LatestEvidenceId = evidence.Id; req.VerificationStatus = FileScanStatuses.Quarantined;
                    gate.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(token);
                    await audit.RecordAsync(new(actor, "REVIEW_GATE_EVIDENCE_QUARANTINED", "REVIEW_GATE", gate.Id,
                        new Dictionary<string, object?> { ["requirementId"] = req.Id, ["version"] = evidence.Version, ["reason"] = scan.Details }), token);
                }
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
        if (!await HasReviewerPermission(ct))
            throw new ForbiddenException("An active reviewer permission is required.");
        for (var attempt = 0; ; attempt++)
        {
            try { return await ReadAssignedReviewsPage(page, pageSize, ct); }
            catch (SqlException ex) when (ex.Number is 1205 or 1222)
            {
                if (attempt >= 2) throw new ConflictException("The reviewer queue is busy; retry shortly.", "REVIEW_QUEUE_BUSY");
                await Task.Delay(TimeSpan.FromMilliseconds(50 * (attempt + 1)), ct);
            }
        }
    }

    private async Task<PagedResult<AssignedReviewItemDto>> ReadAssignedReviewsPage(int page, int pageSize, CancellationToken ct)
    {
        var now = Now;
        var query = db.GateReviewerAssignments.AsNoTracking()
            .Where(x => x.ReviewerId == Actor && x.Status != "DECIDED" && x.Status != "REVOKED"
                && (x.ValidFrom == null || x.ValidFrom <= now)
                && (x.ValidUntil == null || now <= x.ValidUntil)
                && !db.SupervisorAssignments.Any(a => a.ProjectId == x.GateInstance.ProjectId
                    && ((a.IsPrimary && a.AssignmentType == "PRIMARY") || a.EndedAt == null)
                    && a.SupervisorProfile.UserId == Actor));
        var total = await query.LongCountAsync(ct);
        var ids = await query.OrderBy(x => x.GateInstance.WindowEnd).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToArrayAsync(ct);
        if (ids.Length == 0) return new([], page, pageSize, total);

        var rows = await db.GateReviewerAssignments.AsNoTracking().AsSplitQuery()
            .Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence)
            .Where(x => ids.Contains(x.Id) && x.ReviewerId == Actor && x.Status != "DECIDED" && x.Status != "REVOKED"
                && (x.ValidFrom == null || x.ValidFrom <= now)
                && (x.ValidUntil == null || now <= x.ValidUntil))
            .ToArrayAsync(ct);
        var byId = rows.ToDictionary(x => x.Id);
        var projectIds = rows.Select(x => x.GateInstance.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Where(x => projectIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var submitterNames = await LoadSubmitterNamesAsync(rows.Select(x => x.GateInstance), ct);
        var gateIds = rows.Select(x => x.GateInstanceId).Distinct().ToArray();
        var latestPackageByGate = gateIds.Length == 0
            ? new Dictionary<long, long>()
            : (await db.Set<ReviewGatePackage>().AsNoTracking()
                .Where(p => gateIds.Contains(p.GateInstanceId))
                .Select(p => new { p.GateInstanceId, p.Id, p.RoundNumber })
                .ToListAsync(ct))
                .GroupBy(p => p.GateInstanceId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.RoundNumber).First().Id);
        return new(ids.Where(byId.ContainsKey).Select(id =>
            AssignmentDto(byId[id], projects[byId[id].GateInstance.ProjectId], submitterNames,
                latestPackageByGate.TryGetValue(byId[id].GateInstanceId, out var pkgId) ? pkgId : null)).ToArray(), page, pageSize, total);
    }

    public async Task<FileDownload> DownloadEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct)
    {
        await RequireActiveActor(ct);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        var evidence = await db.GateEvidence.AsNoTracking().Include(x => x.Requirement).ThenInclude(x => x.GateInstance)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.Version == version && x.Requirement.GateInstance.Code == gateCode, ct)
            ?? throw new NotFoundException("ReviewGateEvidence", requirementId);
        if (!await IsAdmin(ct))
        {
            if (!await CanReadEvidence(projectId, scope, evidence.Requirement, ct))
            {
                var now = Now;
                var hasRevokedOrExpired = await db.GateReviewerAssignments.AsNoTracking().AnyAsync(
                    x => x.GateInstanceId == evidence.Requirement.GateInstanceId && x.ReviewerId == Actor
                        && (x.Status == "REVOKED"
                            || (x.ValidFrom != null && x.ValidFrom > now)
                            || (x.ValidUntil != null && now > x.ValidUntil)), ct);
                if (hasRevokedOrExpired)
                    throw new ForbiddenException("This review assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
                throw new ForbiddenException("The evidence is outside the review assignment scope.", WorkflowErrorCodes.ScopeAccessDenied);
            }
        }
        if (evidence.ScanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Evidence file is quarantined or not ready for download.", "FILE_QUARANTINED");
        if (string.IsNullOrWhiteSpace(evidence.StorageKey)) throw new ValidationException(new Dictionary<string, string[]> { ["evidence"] = ["This evidence is an external link and has no downloadable file."] });
        try { return new(await storage.OpenReadAsync(evidence.StorageKey, ct), evidence.ContentType, evidence.ArtifactName, evidence.SizeBytes); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException("ReviewGateEvidenceFile", evidence.Id); }
    }

    public async Task<SignedPreviewTokenDto> IssueEvidencePreviewToken(long projectId, string gateCode, long requirementId, int version, CancellationToken ct)
    {
        await RequireActiveActor(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        var evidence = await db.GateEvidence.AsNoTracking().Include(x => x.Requirement).ThenInclude(x => x.GateInstance)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.Version == version && x.Requirement.GateInstance.Code == gateCode, ct)
            ?? throw new NotFoundException("ReviewGateEvidence", requirementId);
        await EnsureCanReadGateEvidenceAsync(projectId, scope, evidence, ct);
        if (evidence.ScanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Evidence file is quarantined or failed preview and cannot be streamed.", "FILE_QUARANTINED");
        var expires = Now.AddMinutes(5);
        var token = previewTokens.Issue(new SignedPreviewClaims("GATE_EVIDENCE", evidence.Id, Actor, null, expires));
        var basePath = $"/api/v1/projects/{projectId}/review-gates/{gateCode}/requirements/{requirementId}/evidence/{version}";
        return new(token, expires, $"{basePath}/stream?token={Uri.EscapeDataString(token)}", $"{basePath}/safe-preview",
            evidence.ContentType, evidence.ArtifactName, evidence.ScanStatus);
    }

    public async Task<FileDownload> StreamEvidence(long projectId, string gateCode, long requirementId, int version, string token, CancellationToken ct)
    {
        await RequireActiveActor(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        var evidence = await db.GateEvidence.AsNoTracking().Include(x => x.Requirement).ThenInclude(x => x.GateInstance)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.Version == version && x.Requirement.GateInstance.Code == gateCode, ct)
            ?? throw new NotFoundException("ReviewGateEvidence", requirementId);
        if (previewTokens.Validate(token, "GATE_EVIDENCE", evidence.Id, Actor) is null)
            throw new ForbiddenException("Preview stream token is invalid, expired, or bound to another user.", WorkflowErrorCodes.Forbidden);
        await EnsureCanReadGateEvidenceAsync(projectId, scope, evidence, ct);
        if (evidence.ScanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Evidence file is quarantined or not ready for streaming.", "FILE_QUARANTINED");
        if (string.IsNullOrWhiteSpace(evidence.StorageKey))
            throw new ValidationException(new Dictionary<string, string[]> { ["evidence"] = ["External links cannot be streamed."] });
        try { return new(await storage.OpenReadAsync(evidence.StorageKey, ct), evidence.ContentType, evidence.ArtifactName, evidence.SizeBytes); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { throw new NotFoundException("ReviewGateEvidenceFile", evidence.Id); }
    }

    public async Task<OfficePreviewDto> SafePreviewEvidence(long projectId, string gateCode, long requirementId, int version, CancellationToken ct)
    {
        await RequireActiveActor(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        var evidence = await db.GateEvidence.AsNoTracking().Include(x => x.Requirement).ThenInclude(x => x.GateInstance)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.RequirementId == requirementId && x.Version == version && x.Requirement.GateInstance.Code == gateCode, ct)
            ?? throw new NotFoundException("ReviewGateEvidence", requirementId);
        await EnsureCanReadGateEvidenceAsync(projectId, scope, evidence, ct);
        if (evidence.ScanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Quarantined or unready evidence cannot be previewed.", "FILE_QUARANTINED");
        if (string.IsNullOrWhiteSpace(evidence.StorageKey))
            throw new ValidationException(new Dictionary<string, string[]> { ["evidence"] = ["External links do not have binary derivatives."] });

        var cached = await db.Set<FilePreviewDerivative>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.GateEvidenceId == evidence.Id, ct);
        if (cached is { Status: "READY", PayloadJson: not null })
            return JsonSerializer.Deserialize<OfficePreviewDto>(cached.PayloadJson)!;
        if (cached is { Status: "PREVIEW_FAILED" })
            throw new ConflictException(cached.ErrorReason ?? "Preview conversion failed for this file.", "PREVIEW_FAILED");

        await using var stream = await storage.OpenReadAsync(evidence.StorageKey, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        try
        {
            var result = officePreview.Preview(evidence.ArtifactName, bytes);
            var derivType = result.Kind switch
            {
                "PPTX_SLIDES" => "PPTX_SLIDES",
                "XLSX_GRID" => "XLSX_GRID",
                "ZIP_TREE" => "ZIP_TREE",
                _ => "OFFICE_TEXT"
            };
            var now = Now;
            db.Set<FilePreviewDerivative>().Add(new FilePreviewDerivative
            {
                GateEvidenceId = evidence.Id,
                DerivativeType = derivType,
                Status = "READY",
                PayloadJson = JsonSerializer.Serialize(result),
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (ValidationException ex)
        {
            var now = Now;
            var reason = ex.Errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Unsupported or unsafe preview content.";
            db.Set<FilePreviewDerivative>().Add(new FilePreviewDerivative
            {
                GateEvidenceId = evidence.Id,
                DerivativeType = "OFFICE_TEXT",
                Status = "PREVIEW_FAILED",
                ErrorReason = reason.Length > 500 ? reason[..500] : reason,
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);
            throw new ConflictException(reason, "PREVIEW_FAILED");
        }
    }

    private async Task EnsureCanReadGateEvidenceAsync(long projectId, ProjectAcademicScope scope, GateEvidence evidence, CancellationToken ct)
    {
        if (await IsAdmin(ct)) return;
        if (await CanReadEvidence(projectId, scope, evidence.Requirement, ct)) return;
        var now = Now;
        var hasRevokedOrExpired = await db.GateReviewerAssignments.AsNoTracking().AnyAsync(
            x => x.GateInstanceId == evidence.Requirement.GateInstanceId && x.ReviewerId == Actor
                && (x.Status == "REVOKED"
                    || (x.ValidFrom != null && x.ValidFrom > now)
                    || (x.ValidUntil != null && now > x.ValidUntil)), ct);
        if (hasRevokedOrExpired)
            throw new ForbiddenException("This review assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
        throw new ForbiddenException("The evidence is outside the review assignment scope.", WorkflowErrorCodes.ScopeAccessDenied);
    }

    public async Task<ReviewGateArchiveManifest> ArchiveManifest(long projectId, string gateCode, CancellationToken ct)
    {
        await RequireActiveActor(ct);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        var gate = await db.ProjectGateInstances.AsNoTracking().AsSplitQuery()
            .Include(g => g.Requirements).ThenInclude(r => r.Evidence)
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == gateCode, ct)
            ?? throw new NotFoundException("ProjectGate", projectId);
        var entries = new List<ReviewGateArchiveEntry>();
        foreach (var requirement in gate.Requirements.OrderBy(r => r.Code).ThenBy(r => r.Id))
        {
            var evidence = requirement.Evidence.OrderByDescending(e => e.Version).FirstOrDefault();
            if (evidence is null) continue;
            if (!await IsAdmin(ct) && !await CanReadEvidence(projectId, scope, requirement, ct))
            {
                var now = Now;
                var hasRevokedOrExpired = await db.GateReviewerAssignments.AsNoTracking().AnyAsync(
                    x => x.GateInstanceId == gate.Id && x.ReviewerId == Actor
                        && (x.Status == "REVOKED"
                            || (x.ValidFrom != null && x.ValidFrom > now)
                            || (x.ValidUntil != null && now > x.ValidUntil)), ct);
                if (hasRevokedOrExpired)
                    throw new ForbiddenException("This review assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
                throw new ForbiddenException("The archive contains evidence outside the actor's academic scope.", WorkflowErrorCodes.ScopeAccessDenied);
            }
            var isLink = string.IsNullOrWhiteSpace(evidence.StorageKey);
            if (isLink && (!Uri.TryCreate(evidence.LinkUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
                throw new ValidationException(new Dictionary<string, string[]> { ["evidence"] = ["The archive contains an invalid external evidence URL."] });
            entries.Add(new(requirement.Id, requirement.Code, requirement.Title, evidence.Version,
                evidence.ArtifactName, isLink, evidence.LinkUrl));
        }
        if (entries.Count == 0) throw new NotFoundException("ReviewGateEvidence", gate.Id);
        return new(project.Code, gate.Code, entries);
    }

    public Task<AssignedReviewItemDto> VerifyEvidence(long assignmentId, VerifyReviewGateEvidenceRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerId == Actor, token)
            ?? throw new NotFoundException("ReviewGateAssignment", assignmentId);
        RequireActiveAssignment(row);
        await RequireReviewerCanAct(row, token);
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        var requirementId = ParseId(input.RequirementId, "requirementId");
        var requirement = row.GateInstance.Requirements.SingleOrDefault(x => x.Id == requirementId) ?? throw new NotFoundException("ReviewGateRequirement", requirementId);
        if (!Covers(row, requirement)) throw new ForbiddenException("The reviewer assignment does not cover this requirement.", WorkflowErrorCodes.ScopeAccessDenied);
        if (!await db.Users.AnyAsync(x => x.Id == Actor && x.Status == "ACTIVE", token)) throw new ForbiddenException("The reviewer account is inactive.");
        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, row.GateInstance.ProjectId, Actor, token))
            throw new ConflictException("A project's current or former primary supervisor or discipline mentor cannot review its own gate (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
        var latestPackage = await db.Set<ReviewGatePackage>().Include(p => p.Items)
            .Where(p => p.GateInstanceId == row.GateInstanceId)
            .OrderByDescending(p => p.RoundNumber)
            .FirstOrDefaultAsync(token);
        if (input.PackageId.HasValue && (latestPackage is null || latestPackage.Id != input.PackageId.Value))
            throw new ConflictException("The review package snapshot is stale. Reload the latest submitted gate snapshot.", WorkflowErrorCodes.StaleEvidenceVersion);

        GateEvidence? evidence;
        if (latestPackage is not null && latestPackage.Items.SingleOrDefault(i => i.RequirementId == requirement.Id) is { } pinnedItem)
        {
            evidence = requirement.Evidence.SingleOrDefault(e => e.Id == pinnedItem.EvidenceId)
                ?? await db.GateEvidence.SingleOrDefaultAsync(e => e.Id == pinnedItem.EvidenceId, token);
        }
        else
        {
            evidence = requirement.Evidence.OrderByDescending(x => x.Version).FirstOrDefault();
        }
        if (evidence is null) throw new ConflictException("No evidence has been submitted.");
        if ((input.EvidenceId.HasValue && input.EvidenceId.Value != evidence.Id)
            || (input.EvidenceVersion.HasValue && input.EvidenceVersion.Value != evidence.Version))
            throw new ConflictException("The evidence version is stale or does not match the frozen gate snapshot.", WorkflowErrorCodes.StaleEvidenceVersion);
        if (evidence.ScanStatus != FileScanStatuses.Ready)
            throw new ConflictException("Quarantined or unready evidence cannot be verified.", "FILE_QUARANTINED");
        if (input.Decision is not ("ACCEPTED" or "REJECTED")) throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["Decision must be ACCEPTED or REJECTED."] });
        db.GateEvidenceVerifications.Add(new GateEvidenceVerification
        {
            EvidenceId = evidence.Id, AssignmentId = row.Id, PackageId = latestPackage?.Id, RequirementId = requirement.Id,
            Decision = input.Decision, Feedback = input.Feedback?.Trim(), ReviewerId = Actor, CreatedAt = Now
        });
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        await RecomputeRequirementAggregateStatusAsync(row.GateInstance, requirement, evidence, latestPackage?.Id, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_EVIDENCE_VERIFIED", "REVIEW_GATE_ASSIGNMENT", row.Id,
            new Dictionary<string, object?> { ["requirementId"] = requirement.Id, ["evidenceId"] = evidence.Id, ["evidenceVersion"] = evidence.Version, ["packageId"] = latestPackage?.Id, ["decision"] = input.Decision }), token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == row.GateInstance.ProjectId, token);
        return AssignmentDto(row, project, await LoadSubmitterNamesAsync([row.GateInstance], token), latestPackage?.Id);
    }, ct);

    private async Task RecomputeRequirementAggregateStatusAsync(
        ProjectGateInstance gate,
        ProjectGateRequirement requirement,
        GateEvidence evidence,
        long? latestPackageId,
        CancellationToken ct)
    {
        var coveringAssignments = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED")
            .ToArrayAsync(ct);
        var coveringIds = coveringAssignments.Where(a => CoversRequirement(a, requirement)).Select(a => a.Id).ToArray();
        if (coveringIds.Length == 0) return;

        var verifications = await db.GateEvidenceVerifications.AsNoTracking()
            .Where(v => coveringIds.Contains(v.AssignmentId)
                && ((latestPackageId.HasValue && v.PackageId == latestPackageId.Value && (v.RequirementId == requirement.Id || v.EvidenceId == evidence.Id))
                    || (!latestPackageId.HasValue && v.EvidenceId == evidence.Id)))
            .OrderByDescending(v => v.Id)
            .ToArrayAsync(ct);

        var latestByAssignment = verifications
            .GroupBy(v => v.AssignmentId)
            .ToDictionary(g => g.Key, g => g.First());

        var reviewerIds = latestByAssignment.Values.Select(v => v.ReviewerId).Distinct().ToArray();
        var reviewerNames = reviewerIds.Length == 0
            ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => reviewerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        if (latestByAssignment.Values.Any(v => v.Decision == "REJECTED"))
        {
            requirement.VerificationStatus = "REJECTED";
            evidence.Status = "REJECTED";
        }
        else if (coveringIds.All(id => latestByAssignment.TryGetValue(id, out var v) && v.Decision == "ACCEPTED"))
        {
            requirement.VerificationStatus = "ACCEPTED";
            evidence.Status = "ACCEPTED";
        }
        else
        {
            requirement.VerificationStatus = "SUBMITTED";
            evidence.Status = "SUBMITTED";
        }

        var notes = latestByAssignment.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.Feedback))
            .OrderBy(v => v.AssignmentId)
            .Select(v => $"[{reviewerNames.GetValueOrDefault(v.ReviewerId, $"PB#{v.ReviewerId}")}] {v.Feedback!.Trim()}")
            .ToArray();
        if (notes.Length > 0)
        {
            var joined = string.Join(" | ", notes);
            requirement.ReviewerFeedback = joined.Length <= 2000 ? joined : joined[..2000];
        }
        await db.SaveChangesAsync(ct);
    }

    public Task<AssignedReviewItemDto> Decide(long assignmentId, SubmitReviewGateDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.Id == assignmentId && x.ReviewerId == Actor, token)
            ?? throw new NotFoundException("ReviewGateAssignment", assignmentId);
        RequireActiveAssignment(row);
        await RequireReviewerCanAct(row, token);
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        if (input.Decision is not ("PASSED" or "REVISION_REQUIRED") || string.IsNullOrWhiteSpace(input.Notes)) throw new ValidationException(new Dictionary<string, string[]> { ["decision"] = ["A decision and notes are required."] });
        var latestPkg = await db.Set<ReviewGatePackage>().AsNoTracking()
            .Where(p => p.GateInstanceId == row.GateInstanceId)
            .OrderByDescending(p => p.RoundNumber)
            .Select(p => new { p.Id, p.RoundNumber })
            .FirstOrDefaultAsync(token);
        var latestPkgId = latestPkg?.Id;
        if (input.PackageId.HasValue && latestPkgId != input.PackageId.Value)
            throw new ConflictException("The review package snapshot is stale.", WorkflowErrorCodes.StaleEvidenceVersion);

        // TK-W2-06: Per-assignment verification check covering only this reviewer's mandatory requirements.
        if (input.Decision == "PASSED")
        {
            var coveredMandatory = row.GateInstance.Requirements
                .Where(r => r.Required && r.VerificationStatus != "WAIVED_WITHDRAWN" && CoversRequirement(row, r))
                .ToArray();
            if (coveredMandatory.Length > 0)
            {
                var myVerifications = await db.GateEvidenceVerifications.AsNoTracking()
                    .Where(v => v.AssignmentId == row.Id
                        && (latestPkgId == null || v.PackageId == latestPkgId))
                    .OrderByDescending(v => v.Id)
                    .ToArrayAsync(token);

                var unaccepted = coveredMandatory.Any(req =>
                {
                    var latestEvId = req.LatestEvidenceId ?? req.Evidence.OrderByDescending(e => e.Version).FirstOrDefault()?.Id;
                    if (!latestEvId.HasValue && req.VerificationStatus == "ACCEPTED") return false;
                    var latestVer = myVerifications.FirstOrDefault(v => v.RequirementId == req.Id || (latestEvId.HasValue && v.EvidenceId == latestEvId.Value));
                    return latestVer is null || latestVer.Decision != "ACCEPTED";
                });
                if (unaccepted)
                    throw new ValidationException(new Dictionary<string, string[]> { ["gate"] = ["All mandatory evidence within your assigned scope must be verified ACCEPTED before passing the gate."] });
            }
        }

        db.GateDecisions.Add(new GateDecision { AssignmentId = row.Id, PackageId = latestPkgId, Decision = input.Decision, Notes = input.Notes.Trim(), DecidedBy = Actor, DecidedAt = Now });
        row.LatestDecision = input.Decision; row.ReviewerNotes = input.Notes.Trim(); row.Status = input.Decision == "PASSED" ? "DECIDED" : "PENDING_REVIEW"; row.ConcurrencyToken = Guid.NewGuid();
        var activeAssignments = await db.GateReviewerAssignments.Where(x => x.GateInstanceId == row.GateInstanceId && x.Status != "REVOKED").ToArrayAsync(token);
        var panel = activeAssignments.Select(x => x.ReviewerId).Distinct().ToArray();

        // Check ParticipatingScope veto on current round (IT_MKT)
        var hasScopeVeto = latestPkg is not null && await db.Set<ProjectGateRoundScopeDecision>().AsNoTracking()
            .AnyAsync(d => d.GateInstanceId == row.GateInstanceId && d.RoundNumber == latestPkg.RoundNumber && d.Decision == "REVISION_REQUIRED", token);

        var gatePassed = input.Decision == "PASSED"
            && !hasScopeVeto
            && (row.GateInstance.Sequence is < 1 or > 3 || panel.Length == 2)
            && activeAssignments.All(x => x.Id == row.Id || x.LatestDecision == "PASSED");
        ResetOverride(row.GateInstance);
        row.GateInstance.Status = input.Decision == "REVISION_REQUIRED" || hasScopeVeto ? "REVISION_REQUIRED" : gatePassed ? "PASSED" : "IN_REVIEW";
        row.GateInstance.ReviewEligibility = row.GateInstance.Status == "REVISION_REQUIRED" ? "BLOCKED" : "READY";
        await db.SaveChangesAsync(token);
        await NotifyGateChanged(row.GateInstance.ProjectId, row.GateInstance.Id, row.GateInstance.Code, $"Quyết định kiểm duyệt cổng {row.GateInstance.Code}: {GateStatusLabel(input.Decision)}.", token, enqueueEmail: true, includeDepartmentStaff: true);
        if (gatePassed) await OpenNextGate(row.GateInstance, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_DECISION_RECORDED", "REVIEW_GATE_ASSIGNMENT", row.Id, new Dictionary<string, object?> { ["decision"] = input.Decision }), token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == row.GateInstance.ProjectId, token);
        return AssignmentDto(row, project, await LoadSubmitterNamesAsync([row.GateInstance], token), latestPkgId);
    }, ct);

    public Task<AssignedReviewItemDto> Assign(CreateReviewGateAssignmentRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        if (!long.TryParse(input.ProjectId, out var projectId) || projectId <= 0 || input.ReviewerId <= 0 || string.IsNullOrWhiteSpace(input.GateCode))
            throw new ValidationException(new Dictionary<string, string[]> { ["assignment"] = ["Project, gate and reviewer are required."] });
        await AcademicAssignmentGuard.LockProjectAsync(db, projectId, token);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        await RequireProjectRead(projectId, scope, token);
        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == input.GateCode, token)
            ?? throw new NotFoundException("ProjectGate", 0);
        if (input.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Unsupported review scope."] });
        if (input.Scope == "MAJOR_SPECIFIC" && (!input.MajorId.HasValue || !scope.MajorIds.Contains(input.MajorId.Value))) throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["Major is outside the frozen project scope."] });
        if (input.Scope == "INDIVIDUAL" && (!input.StudentId.HasValue || !await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == projectId && m.UserId == input.StudentId && m.LeftAt == null, token)))
            throw new ValidationException(new Dictionary<string, string[]> { ["studentId"] = ["Individual assignment must target an active project member."] });
        var normalizedAssignmentScope = input.Scope == "COMMON" ? "COMMON" : input.Scope == "MAJOR_SPECIFIC"
            ? input.MajorId?.ToString() ?? input.Scope
            : (input.StudentId.HasValue ? (await db.Users.Where(x => x.Id == input.StudentId.Value).Select(x => x.MajorId).SingleOrDefaultAsync(token))?.ToString() ?? input.Scope : input.Scope);
        if (input.Scope == "INDIVIDUAL" && normalizedAssignmentScope == "INDIVIDUAL")
            throw new ValidationException(new Dictionary<string, string[]> { ["studentId"] = ["The target student's academic major is required for an individual assignment."] });
        await RequireScopeMutationAuthority(scope, normalizedAssignmentScope, token);
        if (input.SessionRole is not ("MEMBER" or "CHAIR" or "SECRETARY"))
            throw new ValidationException(new Dictionary<string, string[]> { ["sessionRole"] = ["Unsupported gate session role."] });
        if (input.SessionRole is "CHAIR" or "SECRETARY" && !await db.Users.AsNoTracking().AnyAsync(u =>
            u.Id == Actor && u.Status == "ACTIVE" && u.DepartmentId == scope.LeadDepartmentId
            && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), token))
            throw new ForbiddenException("Only lead department staff can appoint a session chair or secretary.");
        var reviewer = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.ReviewerId && x.Status == "ACTIVE"
            && x.UserRoleUsers.Any(r => r.Role.Code == "LECTURER" || r.Role.Code == "DEPARTMENT_STAFF"), token)
            ?? throw new NotFoundException("EligibleReviewer", input.ReviewerId);
        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, projectId, reviewer.Id, token))
            throw new ConflictException("A project's current or former primary supervisor or discipline mentor cannot review its own gate (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
        if (gate.Sequence is >= 1 and <= 3)
        {
            var fixedPanelRows = await db.GateReviewerAssignments.AsNoTracking()
                .Where(a => a.GateInstance.ProjectId == projectId && a.GateInstance.Sequence >= 1
                    && a.GateInstance.Sequence <= 3 && a.Status != "REVOKED")
                .Select(a => new { a.ReviewerId, a.MajorId })
                .ToArrayAsync(token);
            var fixedPanel = fixedPanelRows.Select(a => a.ReviewerId).Distinct().ToArray();
            if (!fixedPanel.Contains(reviewer.Id) && fixedPanel.Length >= 2)
                throw new ConflictException("Gate 1–3 use the same two evaluators for this project.", "GATE_FIXED_PANEL_CONFLICT");

            // QD-09: Interdisciplinary projects (IT_MKT) allow at most 1 reviewer per major/department across Gates 1–3.
            if (scope.MajorIds.Count >= 2 && !fixedPanel.Contains(reviewer.Id))
            {
                if (input.MajorId.HasValue && fixedPanelRows.Any(a => a.MajorId == input.MajorId.Value && a.ReviewerId != reviewer.Id))
                    throw new ConflictException("Interdisciplinary projects allow at most one reviewer per major (QD-09).", WorkflowErrorCodes.GateMajorReviewerLimit);

                if (reviewer.DepartmentId.HasValue && fixedPanel.Length > 0)
                {
                    var existingSameDept = await db.Users.AsNoTracking()
                        .AnyAsync(u => fixedPanel.Contains(u.Id) && u.DepartmentId == reviewer.DepartmentId.Value, token);
                    if (existingSameDept)
                        throw new ConflictException("Interdisciplinary projects require one reviewer from each participating department/major (QD-09).", WorkflowErrorCodes.GateMajorReviewerLimit);
                }
            }
        }
        if (input.Scope == "MAJOR_SPECIFIC" && input.MajorId is not null)
        {
            var majorDepartment = await db.Majors.Where(m => m.Id == input.MajorId).Select(m => (long?)m.DepartmentId).SingleOrDefaultAsync(token);
            var sameDepartment = majorDepartment.HasValue && await db.Users.AnyAsync(u => u.Id == reviewer.Id && u.DepartmentId == majorDepartment, token);
            var assignedMajor = await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == projectId && a.EndedAt == null
                && a.SupervisorProfile.UserId == reviewer.Id && a.MajorId == input.MajorId, token);
            if (!sameDepartment && !assignedMajor) throw new ForbiddenException("Reviewer is outside the requested major scope.");
        }
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.GateInstanceId == gate.Id && x.ReviewerId == reviewer.Id && x.Scope == input.Scope && x.MajorId == input.MajorId && x.StudentId == input.StudentId, token);
        if (row is { SessionRole: "CHAIR" or "SECRETARY" } && row.SessionRole != input.SessionRole
            && !await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE"
                && u.DepartmentId == scope.LeadDepartmentId
                && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), token))
            throw new ForbiddenException("Only lead department staff can change a gate session officer.");
        if (row is null)
        {
            row = new GateReviewerAssignment { GateInstanceId = gate.Id, ReviewerId = reviewer.Id, Scope = input.Scope, MajorId = input.MajorId, StudentId = input.StudentId, SessionRole = input.SessionRole, CreatedAt = Now };
            db.GateReviewerAssignments.Add(row);
        }
        else
        {
            row.MajorId = input.MajorId; row.StudentId = input.StudentId; row.SessionRole = input.SessionRole; row.Status = "PENDING_REVIEW";
            row.LatestDecision = null; row.ReviewerNotes = null; row.RevokedAt = null; row.RevokedBy = null; row.RevocationReason = null;
            row.ConcurrencyToken = Guid.NewGuid();
        }
        await db.SaveChangesAsync(token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == projectId, token);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, Actor, "REVIEW_GATE_ASSIGNED",
            $"Bạn được phân công phản biện cổng {gate.Code} ({project.Code})",
            $"Bạn được Khoa phân công thẩm định cổng {gate.Code} cho đồ án {project.Code} - {project.Title} (Phạm vi: {input.Scope}).",
            "REVIEW_GATE_ASSIGNMENT", row.Id, [reviewer.Id], Now, enqueueEmail: true, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_ASSIGNMENT_CREATED", "REVIEW_GATE_ASSIGNMENT", row.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gate.Code, ["reviewerId"] = reviewer.Id, ["scope"] = input.Scope }), token);
        return AssignmentDto(row, project, await LoadSubmitterNamesAsync([gate], token));
    }, ct);

    public Task<AssignedReviewItemDto> Revoke(long assignmentId, RevokeReviewGateAssignmentRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        if (string.IsNullOrWhiteSpace(input.Reason))
            throw new ValidationException(new Dictionary<string, string[]> { ["reason"] = ["A revocation reason is required."] });
        var row = await db.GateReviewerAssignments.Include(x => x.GateInstance).ThenInclude(x => x.Requirements).ThenInclude(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.Id == assignmentId, token) ?? throw new NotFoundException("ReviewGateAssignment", assignmentId);
        if (row.Status is "REVOKED" or "DECIDED") throw new ConflictException("This review assignment is no longer active.", "REVIEW_ASSIGNMENT_INACTIVE");
        CheckToken(row.ConcurrencyToken, input.ConcurrencyToken);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, row.GateInstance.ProjectId, token, true);
        await RequireProjectRead(row.GateInstance.ProjectId, scope, token);
        row.Status = "REVOKED"; row.RevokedAt = Now; row.RevokedBy = Actor; row.RevocationReason = input.Reason.Trim(); row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == row.GateInstance.ProjectId, token);
        await V4WorkflowNotificationHelper.EmitAsync(
            db, Actor, "REVIEW_GATE_REVOKED",
            $"Phân công phản biện cổng {row.GateInstance.Code} ({project.Code}) đã bị thu hồi",
            $"Phân công thẩm định cổng {row.GateInstance.Code} cho đồ án {project.Code} đã được thu hồi. Lý do: {row.RevocationReason}",
            "REVIEW_GATE_ASSIGNMENT", row.Id, [row.ReviewerId], Now, enqueueEmail: true, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_ASSIGNMENT_REVOKED", "REVIEW_GATE_ASSIGNMENT", row.Id,
            new Dictionary<string, object?> { ["projectId"] = row.GateInstance.ProjectId, ["reason"] = row.RevocationReason }), token);
        return AssignmentDto(row, project, await LoadSubmitterNamesAsync([row.GateInstance], token));
    }, ct);

    public async Task<IReadOnlyList<GateAssignmentSummaryDto>> GateAssignments(long projectId, string gateCode, CancellationToken ct)
    {
        await RequireStaff(ct);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
        await RequireProjectRead(projectId, scope, ct);
        var gate = await db.ProjectGateInstances.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ProjectId == projectId && g.Code == gateCode, ct)
            ?? throw new NotFoundException("ProjectGate", 0);

        var rows = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstanceId == gate.Id)
            .OrderByDescending(a => a.Status != "REVOKED").ThenBy(a => a.Id)
            .ToArrayAsync(ct);

        var userIds = rows.Select(r => r.ReviewerId).Distinct().ToArray();
        var majorIds = rows.Where(r => r.MajorId.HasValue).Select(r => r.MajorId!.Value).Distinct().ToArray();
        var userNames = userIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var majorNames = majorIds.Length == 0 ? new Dictionary<long, string>()
            : await db.Majors.AsNoTracking().Where(m => majorIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        return rows.Select(r => new GateAssignmentSummaryDto(
            r.Id, r.ReviewerId, userNames.GetValueOrDefault(r.ReviewerId, $"GV#{r.ReviewerId}"),
            r.Scope, r.MajorId, r.MajorId.HasValue ? majorNames.GetValueOrDefault(r.MajorId.Value) : null,
            r.StudentId, r.Status, r.SessionRole, r.LatestDecision, r.ReviewerNotes,
            r.ValidFrom, r.ValidUntil, r.ConcurrencyToken.ToString("N"))).ToArray();
    }

    public async Task<PagedResult<GateReviewerCandidateDto>> ReviewerCandidates(
        long projectId, string gateCode, long? majorId, string? search, int page, int pageSize, CancellationToken ct)
    {
        await RequireStaff(ct);
        Page(page, pageSize);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct, requireFrozenScope: false);
        await RequireProjectRead(projectId, scope, ct);
        if (!await db.ProjectGateInstances.AsNoTracking().AnyAsync(g => g.ProjectId == projectId && g.Code == gateCode, ct))
            throw new NotFoundException("ProjectGate", 0);

        long[] targetDepartmentIds;
        if (majorId.HasValue)
        {
            var majorDept = await db.Majors.AsNoTracking()
                .Where(m => m.Id == majorId.Value)
                .Select(m => (long?)m.DepartmentId)
                .SingleOrDefaultAsync(ct);
            if (!majorDept.HasValue || !scope.MajorIds.Contains(majorId.Value))
                throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["Major is outside the project scope."] });
            targetDepartmentIds = [majorDept.Value];
        }
        else
        {
            targetDepartmentIds = scope.DepartmentIds.ToArray();
        }

        // D17 + QD-06: Pre-filter ALL current AND former Primary Supervisors and Discipline Mentors on this project.
        var conflictedUserIds = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.SupervisorProfile.UserId)
            .Distinct()
            .ToArrayAsync(ct);

        var fixedPanelIds = (await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstance.ProjectId == projectId && a.GateInstance.Sequence >= 1
                && a.GateInstance.Sequence <= 3 && a.Status != "REVOKED")
            .Select(a => a.ReviewerId)
            .Distinct()
            .ToArrayAsync(ct)).ToHashSet();

        var query = db.Users.AsNoTracking()
            .Where(u => u.Status == "ACTIVE"
                && u.DepartmentId.HasValue && targetDepartmentIds.Contains(u.DepartmentId.Value)
                && u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive
                && u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER" || r.Role.Code == "DEPARTMENT_STAFF")
                && !conflictedUserIds.Contains(u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u => u.FullName.ToLower().Contains(term) || u.Email.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.FullName, u.Email, u.DepartmentId, DepartmentName = u.Department != null ? u.Department.Name : null })
            .ToArrayAsync(ct);

        return new PagedResult<GateReviewerCandidateDto>(
            items.Select(u => new GateReviewerCandidateDto(
                u.Id, u.FullName, u.Email, u.DepartmentId, u.DepartmentName, fixedPanelIds.Contains(u.Id))).ToArray(),
            page, pageSize, total);
    }

    public Task<StudentReviewTimelineDto> SubmitForReview(long projectId, string gateCode, SubmitReviewGateForReviewRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var idempotencyKey = input.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw new ValidationException(new Dictionary<string, string[]> { ["idempotencyKey"] = ["Idempotency-Key header (1..128 characters) is required to submit a gate package snapshot."] });

        await AcademicAssignmentGuard.LockProjectAsync(db, projectId, token);
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
        if (project.Status != "ACTIVE")
            throw new ConflictException("Review gate packages can only be submitted while the project is ACTIVE (D18).", WorkflowErrorCodes.ProjectNotActive);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectLeader(projectId, token);
        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).ThenInclude(x => x.Evidence).SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token) ?? throw new NotFoundException("ProjectGate", 0);
        if (gate.Status is "PASSED" or "FAILED" || gate.DecisionSource == "OVERRIDE")
            throw new ConflictException("Passed, failed, or overridden review gates are locked. Lead department staff must reopen the gate first.", WorkflowErrorCodes.GateImmutableLocked);
        var submitNow = Now;
        var submitDue = gate.Status == "REVISION_REQUIRED" ? (gate.RevisionDueAt ?? gate.WindowEnd) : (gate.SubmissionDueAt ?? gate.WindowEnd);
        if ((gate.WindowStart.HasValue && submitNow < gate.WindowStart.Value) || (submitDue.HasValue && submitNow > submitDue.Value))
            throw new ConflictException("The review gate submission window is closed.", WorkflowErrorCodes.GateWindowClosed);

        // Collect latest evidence per requirement to compute deterministic payload hash before checking idempotency.
        var latestByReq = gate.Requirements.OrderBy(r => r.Id).Select(r => new
        {
            Req = r,
            Latest = r.Evidence.OrderByDescending(e => e.Version).FirstOrDefault()
        }).ToArray();

        var hashMaterial = $"{projectId}|{gate.Id}|{input.Notes?.Trim() ?? ""}|"
            + string.Join(";", latestByReq.Where(x => x.Latest is not null).Select(x => $"{x.Req.Id}:{x.Latest!.Id}:{x.Latest.Version}:{x.Latest.Sha256}"));
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashMaterial))).ToLowerInvariant();

        var existingPkg = await db.Set<ReviewGatePackage>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.GateInstanceId == gate.Id && p.IdempotencyKey == idempotencyKey, token);
        if (existingPkg is not null)
        {
            if (!string.Equals(existingPkg.RequestHash, requestHash, StringComparison.Ordinal))
                throw new ConflictException("This Idempotency-Key was already used with a different gate package payload.", WorkflowErrorCodes.IdempotencyConflict);
            var existingTimeline = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence)
                .Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
            return await TimelineDto(project, scope, existingTimeline, token);
        }

        CheckToken(gate.ConcurrencyToken, input.ConcurrencyToken);
        if (gate.ReviewEligibility == "BLOCKED" && gate.Status != "REVISION_REQUIRED") throw new ConflictException("The previous gate must be passed before submitting this gate.");
        var missingOrUnready = latestByReq
            .Where(x => x.Req.Required && x.Req.VerificationStatus != "WAIVED_WITHDRAWN" && (x.Latest is null || x.Latest.ScanStatus != FileScanStatuses.Ready))
            .Select(x => new BlockingRequirement(
                x.Latest is null ? $"MISSING_EVIDENCE:{x.Req.Code}" : $"EVIDENCE_NOT_READY:{x.Req.Code}:{x.Latest.ScanStatus}",
                x.Latest is null
                    ? $"Mandatory evidence '{x.Req.Title}' has not been uploaded."
                    : $"Mandatory evidence '{x.Req.Title}' is '{x.Latest.ScanStatus}' and must be READY."))
            .ToArray();
        if (missingOrUnready.Length > 0)
            throw new DomainException("Every mandatory evidence item must be uploaded and READY before submitting the gate snapshot.",
                "EVIDENCE_NOT_READY", new ProblemMetadata(BlockingRequirements: missingOrUnready));

        var openCriticalComments = await db.Set<EvidenceAnchoredComment>().AsNoTracking()
            .CountAsync(c => c.ProjectId == projectId && c.GateInstanceId == gate.Id
                && c.Severity == "CRITICAL" && c.Status == "OPEN", token);
        if (openCriticalComments > 0)
            throw new DomainException(
                $"Cannot submit gate '{gate.Code}' while {openCriticalComments} CRITICAL anchored comment(s) remain OPEN.",
                WorkflowErrorCodes.UnresolvedCriticalComments);

        var maxRound = await db.Set<ReviewGatePackage>().Where(p => p.GateInstanceId == gate.Id).MaxAsync(p => (int?)p.RoundNumber, token) ?? 0;
        var now = Now;
        var package = new ReviewGatePackage
        {
            ProjectId = projectId,
            GateInstanceId = gate.Id,
            RoundNumber = maxRound + 1,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            Notes = input.Notes?.Trim(),
            SubmittedBy = Actor,
            SubmittedAt = now,
            Items = latestByReq
                .Where(x => x.Latest is not null && x.Latest.ScanStatus == FileScanStatuses.Ready)
                .Select(x => new ReviewGatePackageItem
                {
                    RequirementId = x.Req.Id,
                    EvidenceId = x.Latest!.Id,
                    EvidenceVersion = x.Latest.Version,
                    Sha256 = x.Latest.Sha256
                }).ToList()
        };
        db.Set<ReviewGatePackage>().Add(package);

        ResetOverride(gate); gate.Status = "IN_REVIEW"; gate.ReviewEligibility = "READY"; gate.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(token);
        await NotifyGateChanged(projectId, gate.Id, gate.Code, "Hồ sơ cổng kiểm duyệt đã được nộp để đánh giá.", token);
        await ReopenAssignmentsForRevision(gate, token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_SUBMITTED_FOR_REVIEW", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gateCode, ["packageId"] = package.Id, ["roundNumber"] = package.RoundNumber, ["notes"] = input.Notes }), token);
        var current = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements).ThenInclude(x => x.Evidence).Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(token);
        return await TimelineDto(project, scope, current, token);
    }, ct);

    public Task<SupervisorFeedbackDto> AddFeedback(long projectId, string gateCode, AddReviewGateFeedbackRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        var text = input.Feedback?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000)
            throw new ValidationException(new Dictionary<string, string[]> { ["feedback"] = ["Feedback must contain 1 to 2000 characters."] });
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId, token) ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true);
        var gate = await db.ProjectGateInstances.AsNoTracking().Include(x => x.Requirements)
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token) ?? throw new NotFoundException("ProjectGate", 0);
        var assignments = await db.SupervisorAssignments.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor)
            .Select(x => new { x.Id, x.AssignmentType, x.IsPrimary, x.MajorId }).ToArrayAsync(token);
        var assignment = assignments.FirstOrDefault(x => x.AssignmentType == "PRIMARY" && x.IsPrimary)
            ?? assignments.FirstOrDefault(x => x.AssignmentType == "DISCIPLINE_MENTOR" && x.MajorId.HasValue
                && gate.Requirements.Any(r => r.Scope == "MAJOR_SPECIFIC" && r.MajorId == x.MajorId));
        if (assignment is null) throw new ForbiddenException("The supervisor is outside this gate's academic scope.");
        var row = new AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorFeedback
        {
            ProjectId = projectId, GateCode = gateCode, SupervisorAssignmentId = assignment.Id,
            FeedbackText = text, CreatedAt = Now, UpdatedAt = Now
        };
        db.SupervisorFeedbacks.Add(row);
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_FEEDBACK_ADDED", "REVIEW_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = projectId, ["gateCode"] = gateCode }), token);
        var name = await db.Users.AsNoTracking().Where(x => x.Id == Actor).Select(x => x.FullName).SingleAsync(token);
        return new SupervisorFeedbackDto(gateCode, text, row.CreatedAt, name);
    }, ct);

    public Task<ProjectReviewMatrixRowDto> UpdateDecision(long projectId, string gateCode, UpdateReviewGateDecisionRequest input, CancellationToken ct) => transactions.InTransactionAsync<ProjectReviewMatrixRowDto>(async token =>
    {
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE"
            && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), token))
            throw new ForbiddenException("Academic staff authority is required for a gate decision.");
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectRead(projectId, scope, token);
        if (input.NewStatus is not ("PASSED" or "REVISION_REQUIRED" or "IN_REVIEW")) throw new ValidationException(new Dictionary<string, string[]> { ["newStatus"] = ["Unsupported gate status."] });
        var gate = await db.ProjectGateInstances.Include(x => x.Requirements).SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == gateCode, token) ?? throw new NotFoundException("ProjectGate", 0);
        await RequireGateMutationAuthority(scope, gate, token);
        throw new ConflictException(
            "Direct gate status updates are disabled to prevent bypassing the two-reviewer panel. Use reviewer decisions, round-scope decisions, or the audited Lead Override workflow.",
            WorkflowErrorCodes.UseOverrideOrRoundDecision);
    }, ct);

    public Task<ProjectReviewMatrixRowDto> UpdateScope(long projectId, string scopeCode, UpdateReviewGateScopeRequest input, CancellationToken ct) => transactions.InTransactionAsync(async token =>
    {
        await RequireStaff(token);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, token, true); await RequireProjectRead(projectId, scope, token);
        if (scope.MajorIds.Count < 2) throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Scope decisions require an interdisciplinary project."] });
        if (input.NewStatus is not ("PASSED" or "REVISION_REQUIRED" or "IN_REVIEW")) throw new ValidationException(new Dictionary<string, string[]> { ["newStatus"] = ["Unsupported scope status."] });
        var majorId = long.TryParse(scopeCode, out var parsed) ? parsed : await db.Majors.Where(x => x.Code == scopeCode || x.Name == scopeCode).Select(x => (long?)x.Id).SingleOrDefaultAsync(token);
        var normalized = scopeCode.Equals("COMMON", StringComparison.OrdinalIgnoreCase) ? "COMMON" : majorId.HasValue && scope.MajorIds.Contains(majorId.Value) ? majorId.Value.ToString() : throw new ValidationException(new Dictionary<string, string[]> { ["scope"] = ["Scope is outside the frozen project scope."] });
        await RequireScopeMutationAuthority(scope, normalized, token);
        var row = await db.ProjectGateScopeDecisions.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Scope == normalized, token);
        if (row is null) { row = new ProjectGateScopeDecision { ProjectId = projectId, Scope = normalized, MajorId = normalized == "COMMON" ? null : long.Parse(normalized), Status = input.NewStatus, DecidedBy = Actor, DecidedAt = Now }; db.ProjectGateScopeDecisions.Add(row); }
        else { CheckToken(row.ConcurrencyToken, input.ConcurrencyToken); row.Status = input.NewStatus; row.DecidedBy = Actor; row.DecidedAt = Now; row.ConcurrencyToken = Guid.NewGuid(); }
        await db.SaveChangesAsync(token); await audit.RecordAsync(new(Actor, "REVIEW_GATE_SCOPE_UPDATED", "PROJECT_GATE_SCOPE", row.Id, new Dictionary<string, object?> { ["projectId"] = projectId, ["scope"] = normalized, ["status"] = input.NewStatus }), token); return await MatrixRow(projectId, token);
    }, ct);

    public async Task<IReadOnlyList<ProjectReviewMatrixRowDto>> Matrix(string? mode, long? departmentId, CancellationToken ct)
    {
        await RequireStaff(ct);
        if (!await IsAdmin(ct))
        {
            var actorDepartment = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
            if (!actorDepartment.HasValue) throw new ForbiddenException("The account has no academic department scope.");
            if (departmentId.HasValue && actorDepartment != departmentId) throw new ForbiddenException("The account is outside the requested department scope.");
            departmentId = actorDepartment;
        }
        var projectIds = await db.ProjectGateInstances.AsNoTracking()
            .Where(x => (departmentId == null || x.Project.ProjectMajors.Any(pm => pm.Major.DepartmentId == departmentId.Value))
                && (mode == null || mode == "ALL" || (mode == "INTERDISCIPLINARY" && x.Project.ProjectMajors.Count > 1) || (mode == "SINGLE_MAJOR" && x.Project.ProjectMajors.Count == 1)))
            .Select(x => x.ProjectId).Distinct().ToArrayAsync(ct);
        var result = new List<ProjectReviewMatrixRowDto>(projectIds.Length);
        foreach (var projectId in projectIds) result.Add(await MatrixRow(projectId, ct));
        return result;
    }

    public async Task<DefenseReadinessDto> DefenseReadiness(long projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId, ct)
            ?? throw new NotFoundException("Project", projectId);
        var scope = await ProjectAcademicScopeReader.ReadAsync(db, projectId, ct);
        await RequireProjectRead(projectId, scope, ct);
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(ct);
        var conditions = gates.Where(g => g.Sequence < 4).Select(g => new DefenseConditionDto(g.Code, g.Title,
            !g.BlocksDefense || g.Status == "PASSED", !g.BlocksDefense || g.Status == "PASSED" ? "Đã đạt hoặc không chặn bảo vệ" : "Chưa đạt Review Gate", g.BlocksDefense)).ToList();
        if (!gates.Any(g => g.Sequence == 3 && g.Status == "PASSED"))
            conditions.Add(new("REVIEW_GATE_3_PASSED", "Đạt Review Gate 3", false,
                "Chưa đạt Review Gate 3.", true));
        if (gates.Length == 0)
            conditions.Add(new("REVIEW_GATES_CONFIGURED", "Cấu hình Review Gates", false,
                "Chưa áp dụng template Review Gates cho project.", true));
        var hasLockedPackage = await db.Set<FinalSubmission>().AsNoTracking()
            .AnyAsync(x => x.ProjectId == projectId && x.Items.Any(), ct);
        conditions.Add(new("FINAL_PACKAGE", "Hồ sơ bàn giao cuối kỳ", hasLockedPackage,
            hasLockedPackage ? "Đã khóa hồ sơ bàn giao" : "Chưa có hồ sơ bàn giao đã khóa", true));
        var committeeConfirmed = await db.Set<DefenseCommittee>().AsNoTracking()
            .AnyAsync(c => c.ProjectId == projectId && c.Status == "CONFIRMED", ct);
        conditions.Add(new("DEFENSE_COMMITTEE", "Hội đồng bảo vệ", committeeConfirmed,
            committeeConfirmed ? "Hội đồng đã xác nhận" : "Bộ môn chưa xác nhận hội đồng", true));
        var coldProgress = await cold.ProgressCore(projectId, ct, system: true);
        conditions.Add(new("COLD_READY", "Chấm Cold", coldProgress.Ready,
            coldProgress.Ready ? "Gói Cold đã khóa và đủ bài chấm" : string.Join(", ", coldProgress.Blockers), true));
        // TK-W2-06: Dual-department consensus is unified into Gate 3 PASSED; no legacy project_gate_scope_decisions blocker.
        return new(projectId.ToString(), project.Title, conditions.All(x => x.Satisfied == true), conditions.Count(x => x.Satisfied != true), conditions, project.Code);
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
        if (current.Length > 0)
        {
            // Self-heal legacy blueprint-created gates that have 0 requirements when their template defines requirements.
            if (current.All(g => g.Requirements.Count == 0))
            {
                var templateIds = current.Select(g => g.TemplateId).Distinct().ToArray();
                var templatesById = await db.ReviewGateTemplates.AsNoTracking()
                    .Include(x => x.Gates).ThenInclude(x => x.Requirements)
                    .Where(x => templateIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, ct);
                var healMembersRaw = await db.TeamMembers.AsNoTracking()
                    .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
                    .OrderBy(m => m.UserId)
                    .Select(m => new { m.UserId, m.User.MajorId })
                    .ToArrayAsync(ct);
                var healMembers = healMembersRaw.Select(m => (m.UserId, m.MajorId)).ToArray();
                var healed = false;
                foreach (var gate in current)
                {
                    if (!templatesById.TryGetValue(gate.TemplateId, out var tpl)) continue;
                    var def = tpl.Gates.FirstOrDefault(g => string.Equals(g.Code, gate.Code, StringComparison.OrdinalIgnoreCase) || g.Sequence == gate.Sequence);
                    if (def is null || def.Requirements.Count == 0) continue;
                    var cloned = CloneTemplateRequirements(def.Requirements, healMembers, scope.MajorIds);
                    foreach (var req in cloned) gate.Requirements.Add(req);
                    if (cloned.Count > 0) healed = true;
                }
                if (healed) await db.SaveChangesAsync(ct);
            }
            return current;
        }
        var mode = scope.MajorIds.Count > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var semesterId = await db.Teams.AsNoTracking().Where(t => t.Id == project.TeamId).Select(t => (long?)t.AcademicSemesterId).SingleOrDefaultAsync(ct);
        var execPeriodId = semesterId.HasValue
            ? await db.ProjectPeriods.AsNoTracking()
                .Where(p => p.AcademicSemesterId == semesterId.Value && p.PeriodType == "EXECUTION" && p.Status != "ARCHIVED")
                .OrderBy(p => p.StartAt).ThenBy(p => p.Id)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(ct)
            : null;
        var regPeriodId = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(x => x.ProjectId == project.Id)
            .OrderByDescending(x => x.Id).Select(x => (long?)x.ProjectPeriodId).FirstOrDefaultAsync(ct);

        async Task<ReviewGateTemplate[]> LoadCandidatesAsync(long? periodFilter)
        {
            var q = db.ReviewGateTemplates.Include(x => x.Gates).ThenInclude(x => x.Requirements)
                .Where(x => x.Status == "PUBLISHED" && x.Mode == mode);
            q = periodFilter.HasValue ? q.Where(x => x.ProjectPeriodId == periodFilter.Value) : q.Where(x => x.ProjectPeriodId == null);
            return await q.OrderByDescending(x => x.Version).ThenByDescending(x => x.UpdatedAt).ToArrayAsync(ct);
        }

        var candidates = execPeriodId.HasValue ? await LoadCandidatesAsync(execPeriodId) : [];
        if (candidates.Length == 0 && regPeriodId.HasValue && regPeriodId != execPeriodId)
            candidates = await LoadCandidatesAsync(regPeriodId);
        if (candidates.Length == 0)
            candidates = await LoadCandidatesAsync(null);
        var major = scope.MajorIds.Count == 1
            ? await db.Majors.AsNoTracking().Where(x => x.Id == scope.MajorIds[0]).Select(x => new { x.Id, x.Code, x.Name }).SingleOrDefaultAsync(ct)
            : null;
        string[] exactTokens = major is null ? [] : [major.Id.ToString(), major.Code, major.Name];
        var exact = candidates.Where(x => exactTokens.Contains(x.Major, StringComparer.OrdinalIgnoreCase)).ToArray();
        var generic = candidates.Where(x => string.IsNullOrWhiteSpace(x.Major)).ToArray();
        ReviewGateTemplate? interdisciplinary = null;
        if (scope.MajorIds.Count > 1)
        {
            var projectMajors = await db.Majors.AsNoTracking()
                .Where(x => scope.MajorIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToArrayAsync(ct);
            var projectCodes = projectMajors.Select(m => m.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

            interdisciplinary = candidates.FirstOrDefault(c =>
            {
                if (string.IsNullOrWhiteSpace(c.Major) || c.Major.Equals("INTERDISCIPLINARY", StringComparison.OrdinalIgnoreCase))
                    return false;
                var tokens = c.Major.Split(new[] { '+', ',', '/', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return projectCodes.All(code => tokens.Contains(code, StringComparer.OrdinalIgnoreCase));
            });

            interdisciplinary ??= candidates.FirstOrDefault(x => x.Major.Equals("INTERDISCIPLINARY", StringComparison.OrdinalIgnoreCase))
                               ?? candidates.FirstOrDefault(x => string.IsNullOrWhiteSpace(x.Major));
        }
        var template = scope.MajorIds.Count > 1 ? interdisciplinary : exact.FirstOrDefault() ?? (generic.Length == 1 ? generic[0] : null);
        if (template is null) return [];
        var activeMembersRaw = await db.TeamMembers.AsNoTracking()
            .Where(m => m.TeamId == project.TeamId && m.LeftAt == null)
            .OrderBy(m => m.UserId)
            .Select(m => new { m.UserId, m.User.MajorId })
            .ToArrayAsync(ct);
        var activeMembers = activeMembersRaw.Select(m => (m.UserId, m.MajorId)).ToArray();
        var orderedDefs = template.Gates.OrderBy(x => x.Sequence).ToArray();
        var created = orderedDefs.Select((g, idx) =>
        {
            var openImmediately = idx == 0 || !g.BlockNextGate;
            return new ProjectGateInstance
            {
                ProjectId = project.Id, TemplateId = template.Id, TemplateVersion = template.Version, Code = g.Code, Sequence = g.Sequence,
                Title = g.Title, Objective = g.Objective, Status = openImmediately ? "PLANNED" : "LOCKED",
                ReviewEligibility = openImmediately ? "READY" : "BLOCKED", Required = g.Required, BlockNextGate = g.BlockNextGate,
                BlocksDefense = g.BlocksDefense, Weight = g.Weight, WindowStart = g.WindowStart, WindowEnd = g.WindowEnd,
                CreatedAt = Now, Requirements = CloneTemplateRequirements(g.Requirements, activeMembers, scope.MajorIds)
            };
        }).ToArray();
        db.ProjectGateInstances.AddRange(created); await db.SaveChangesAsync(ct); return created;
    }

    private static List<ProjectGateRequirement> CloneTemplateRequirements(
        IEnumerable<ReviewGateRequirement> source,
        IReadOnlyList<(long UserId, long? MajorId)> activeMembers,
        IReadOnlyList<long> scopeMajorIds)
    {
        var list = new List<ProjectGateRequirement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long? singleScopeMajorId = scopeMajorIds.Count == 1 ? scopeMajorIds[0] : null;
        foreach (var r in source.OrderBy(x => x.Id))
        {
            if (string.Equals(r.TargetCardinality, "EACH_STUDENT", StringComparison.OrdinalIgnoreCase) && activeMembers.Count > 0)
            {
                foreach (var member in activeMembers)
                {
                    var raw = $"{r.Code}-S{member.UserId}";
                    var code = raw.Length <= 50 ? raw : raw[..50];
                    if (!seen.Add(code)) continue;
                    list.Add(new ProjectGateRequirement
                    {
                        Code = code, Title = r.Title, Type = r.Type, Scope = r.Scope, MajorId = r.MajorId ?? member.MajorId ?? singleScopeMajorId,
                        TargetStudentId = member.UserId, Required = r.Required, TargetCardinality = r.TargetCardinality,
                        DeliveryStage = r.DeliveryStage, FeedsCold = r.FeedsCold, FeedsDefense = r.FeedsDefense,
                        InputKind = r.InputKind, TemplateDocumentId = r.TemplateDocumentId,
                        CarryForwardFromCode = r.CarryForwardFromCode, PolicyTag = r.PolicyTag, RubricCriterionRef = r.RubricCriterionRef
                    });
                }
            }
            else
            {
                var code = r.Code.Length <= 50 ? r.Code : r.Code[..50];
                if (!seen.Add(code)) continue;
                var resolvedMajorId = r.MajorId;
                if (!resolvedMajorId.HasValue && r.TargetStudentId.HasValue)
                    resolvedMajorId = activeMembers.FirstOrDefault(m => m.UserId == r.TargetStudentId.Value).MajorId ?? singleScopeMajorId;
                list.Add(new ProjectGateRequirement
                {
                    Code = code, Title = r.Title, Type = r.Type, Scope = r.Scope, MajorId = resolvedMajorId,
                    TargetStudentId = r.TargetStudentId, Required = r.Required, TargetCardinality = r.TargetCardinality,
                    DeliveryStage = r.DeliveryStage, FeedsCold = r.FeedsCold, FeedsDefense = r.FeedsDefense,
                    InputKind = r.InputKind, TemplateDocumentId = r.TemplateDocumentId,
                    CarryForwardFromCode = r.CarryForwardFromCode, PolicyTag = r.PolicyTag, RubricCriterionRef = r.RubricCriterionRef
                });
            }
        }
        return list;
    }

    private async Task<StudentReviewTimelineDto> TimelineDto(AIPMS.Infrastructure.Persistence.Generated.Models.Project project, ProjectAcademicScope scope, ProjectGateInstance[] gates, CancellationToken ct)
    {
        var current = gates.FirstOrDefault(x => x.Status != "PASSED") ?? gates.LastOrDefault();
        var leader = await db.TeamMembers.AnyAsync(x => x.TeamId == project.TeamId && x.UserId == Actor && x.IsLeader && x.LeftAt == null, ct);
        var mode = scope.MajorIds.Count > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR";
        var majorNames = await db.Majors.AsNoTracking().Where(x => scope.MajorIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Name).ToArrayAsync(ct);
        var policyVersion = gates.FirstOrDefault()?.TemplateVersion.ToString("0.##") ?? "UNKNOWN";
        var submitterNames = await LoadSubmitterNamesAsync(gates, ct);
        var gateIds = gates.Select(g => g.Id).ToArray();
        var allPackages = gateIds.Length == 0
            ? []
            : await db.Set<ReviewGatePackage>().AsNoTracking()
                .Where(p => gateIds.Contains(p.GateInstanceId))
                .Select(p => new { p.GateInstanceId, p.Id, p.RoundNumber })
                .ToListAsync(ct);
        var latestPackages = allPackages
            .GroupBy(p => p.GateInstanceId)
            .ToDictionary(grp => grp.Key, grp => grp.OrderByDescending(p => p.RoundNumber).First());
        var allReqIds = gates.SelectMany(g => g.Requirements).Select(r => r.Id).Distinct().ToArray();
        var rawVerifications = allReqIds.Length == 0
            ? []
            : await db.GateEvidenceVerifications.AsNoTracking()
                .Where(v => v.RequirementId.HasValue && allReqIds.Contains(v.RequirementId.Value))
                .Join(db.Users.AsNoTracking(), v => v.ReviewerId, u => u.Id,
                    (v, u) => new { v.Id, RequirementId = v.RequirementId!.Value, v.AssignmentId, v.PackageId, v.ReviewerId, ReviewerName = u.FullName, v.Decision, v.Feedback, v.CreatedAt })
                .OrderByDescending(v => v.Id)
                .ToArrayAsync(ct);
        var verificationsByReq = rawVerifications
            .GroupBy(v => v.RequirementId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<GateEvidenceVerificationSummaryDto>)g
                    .GroupBy(v => v.AssignmentId)
                    .Select(ag => ag.First())
                    .OrderBy(v => v.AssignmentId)
                    .Select(v => new GateEvidenceVerificationSummaryDto(v.AssignmentId, v.ReviewerId, v.ReviewerName, v.Decision, v.Feedback, DateTime.SpecifyKind(v.CreatedAt, DateTimeKind.Utc)))
                    .ToArray());
        var timelineGatesList = new List<StudentGateInstanceDto>(gates.Length);
        foreach (var g in gates)
        {
            var visibleRequirements = new List<ProjectGateRequirement>(g.Requirements.Count);
            foreach (var r in g.Requirements)
            {
                if (await CanReadEvidence(project.Id, scope, r, ct, forTimeline: true))
                    visibleRequirements.Add(r);
            }
            var blockingReasons = g.ReviewEligibility == "BLOCKED"
                ? (IReadOnlyList<ReviewGateBlockingReasonDto>)[new("PREVIOUS_GATE_REQUIRED", null, "Cổng trước chưa đạt.")]
                : visibleRequirements.Where(x => x.Required && x.VerificationStatus != "ACCEPTED")
                    .Select(x => new ReviewGateBlockingReasonDto("EVIDENCE_REQUIRED", x.Code, "Minh chứng bắt buộc chưa được chấp thuận.")).ToArray();
            var pkg = latestPackages.GetValueOrDefault(g.Id);
            timelineGatesList.Add(new StudentGateInstanceDto(
                g.Id.ToString(), g.Code, g.Title, g.Objective, g.Status, g.ReviewEligibility, g.Required,
                g.WindowStart, g.WindowEnd, blockingReasons,
                visibleRequirements.Select(r => new StudentGateRequirementDto(
                    r.Id.ToString(), r.Code, r.Title, r.Type, r.Scope, r.Required,
                    r.VerificationStatus, Artifact(r, g.Code, submitterNames), r.ReviewerFeedback,
                    r.MajorId, r.TargetStudentId, verificationsByReq.GetValueOrDefault(r.Id))).ToArray(),
                g.ConcurrencyToken.ToString("N"), g.Weight, g.DecisionSource, pkg?.Id, pkg?.RoundNumber));
        }
        var timelineGates = timelineGatesList.ToArray();
        var weights = gates.Select(g => g.Weight).ToArray();
        var configured = gates.Length > 0 && weights.All(x => x.HasValue) && weights.Sum(x => x ?? 0) > 0;
        var academicProgress = configured ? Math.Round(gates.Sum(g => g.Status == "PASSED" ? g.Weight ?? 0 : 0) * 100m / weights.Sum(x => x ?? 0), 2) : (decimal?)null;
        var academicSource = configured ? "FROZEN_GATE_WEIGHTS" : "NOT_CONFIGURED";
        var taskCount = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == project.Id && x.Status != "CANCELLED", ct);
        var doneTasks = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == project.Id && (x.Status == "DONE" || x.Status == "COMPLETED"), ct);
        var taskProgress = taskCount == 0 ? (decimal?)null : Math.Round(doneTasks * 100m / taskCount, 2);
        var disciplineProgress = new List<DisciplineBranchProgressDto>();
        var commonGates = gates.Where(g => g.Requirements.Any(r => r.Scope == "COMMON")).ToArray();
        disciplineProgress.Add(BranchProgress("COMMON", "Phạm vi chung", commonGates));
        foreach (var major in scope.MajorIds.Select((id, index) => (id, name: majorNames.ElementAtOrDefault(index) ?? id.ToString())))
            disciplineProgress.Add(BranchProgress($"MAJOR:{major.id}", major.name,
                gates.Where(g => g.Requirements.Any(r => r.Scope == "MAJOR_SPECIFIC" && r.MajorId == major.id)).ToArray()));
        var defenseReady = gates.Length == 0 ? (bool?)null : gates.Any(g => g.Sequence == 3 && g.Status == "PASSED")
            && gates.Where(g => g.Sequence < 4 && g.BlocksDefense).All(g => g.Status == "PASSED");
        var feedbacks = await db.Set<AIPMS.Infrastructure.Persistence.Generated.Models.SupervisorFeedback>().AsNoTracking()
            .Where(x => x.ProjectId == project.Id).OrderByDescending(x => x.CreatedAt).Take(20)
            .Select(x => new SupervisorFeedbackDto(x.GateCode ?? "GENERAL", x.FeedbackText, x.CreatedAt, x.SupervisorAssignment.SupervisorProfile.User.FullName)).ToArrayAsync(ct);
        return new(project.Id.ToString(), project.Code, project.Title, mode, string.Join(" + ", majorNames), policyVersion, current?.Code ?? "", timelineGates,
            new(leader && current is not null && current.ReviewEligibility == "READY", leader && current is not null && current.Status == "REVISION_REQUIRED", leader ? [] : ["Chỉ trưởng nhóm mới được nộp minh chứng."]),
            academicProgress, academicSource, defenseReady, defenseReady is null ? "NOT_CONFIGURED" : defenseReady.Value ? "READY" : "BLOCKED", disciplineProgress, feedbacks, taskProgress);
    }

    private static DisciplineBranchProgressDto BranchProgress(string scope, string name, IReadOnlyList<ProjectGateInstance> gates)
    {
        var weighted = gates.Where(x => x.Weight.HasValue).ToArray();
        if (weighted.Length == 0) return new(scope, name, null, null, "NOT_CONFIGURED");
        var total = weighted.Sum(x => x.Weight!.Value);
        var progress = total <= 0 ? (decimal?)null : Math.Round(weighted.Sum(x => x.Status == "PASSED" ? x.Weight!.Value : 0) * 100m / total, 2);
        return new(scope, name, progress, Math.Round(total, 2), progress == 100m ? "PASSED" : "IN_PROGRESS");
    }

    private async Task<IReadOnlyDictionary<long, string>> LoadSubmitterNamesAsync(IEnumerable<ProjectGateInstance> gates, CancellationToken ct)
    {
        var userIds = gates.SelectMany(g => g.Requirements).SelectMany(r => r.Evidence)
            .Select(e => e.SubmittedBy).Distinct().ToArray();
        if (userIds.Length == 0) return new Dictionary<long, string>();
        return await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }

    private static IReadOnlyList<ReviewGateBlockingReasonDto> Blocking(ProjectGateInstance g) => g.ReviewEligibility == "BLOCKED" ? [new("PREVIOUS_GATE_REQUIRED", null, "Cổng trước chưa đạt.")] : g.Requirements.Where(x => x.Required && x.VerificationStatus != "ACCEPTED").Select(x => new ReviewGateBlockingReasonDto("EVIDENCE_REQUIRED", x.Code, "Minh chứng bắt buộc chưa được chấp thuận.")).ToArray();
    private static ReviewGateArtifactDto? Artifact(ProjectGateRequirement r, string gateCode, IReadOnlyDictionary<long, string>? submitterNames = null)
    {
        var latest = r.Evidence.OrderByDescending(x => x.Version).FirstOrDefault();
        return latest is null ? null : new(latest.Version, latest.ArtifactName, latest.SubmittedAt, latest.SubmittedBy.ToString(), latest.Notes,
            latest.ContentType, latest.SizeBytes, latest.Sha256,
            $"/api/v1/projects/{latest.ProjectId}/review-gates/{gateCode}/requirements/{latest.RequirementId}/evidence/{latest.Version}/download",
            latest.LinkUrl, latest.Id, latest.ScanStatus, latest.ScanDetails,
            submitterNames is not null && submitterNames.TryGetValue(latest.SubmittedBy, out var name) ? name : null);
    }
    private static void CheckToken(Guid current, string? input) { if (!Guid.TryParse(input, out var value) || value != current) throw new ConflictException("The review gate changed; reload before retrying."); }
    private static void ResetOverride(ProjectGateInstance gate)
    {
        gate.DecisionSource = "REVIEW";
        gate.OverriddenBy = null;
        gate.OverriddenAt = null;
        gate.OverrideReason = null;
    }
    private void RequireActiveAssignment(GateReviewerAssignment row)
    {
        if (row.Status == "REVOKED" || !IsAssignmentWithinValidity(row))
            throw new ForbiddenException("This review assignment has been revoked or expired.", WorkflowErrorCodes.AssignmentRevoked);
        if (row.Status == "DECIDED")
            throw new ConflictException("This review assignment is no longer active.", "REVIEW_ASSIGNMENT_INACTIVE");
    }

    private async Task RequireReviewerCanAct(GateReviewerAssignment row, CancellationToken ct)
    {
        if (!await HasReviewerPermission(ct))
            throw new ForbiddenException("An active reviewer permission is required.");
        if (await AcademicAssignmentGuard.HasSupervisorOrMentorConflictAsync(db, row.GateInstance.ProjectId, Actor, ct))
            throw new ConflictException("A project's current or former primary supervisor or discipline mentor cannot review its own gate (D17).", WorkflowErrorCodes.EvaluatorRoleConflict);
    }

    private Task<bool> HasReviewerPermission(CancellationToken ct) => db.Users.AnyAsync(x => x.Id == Actor && x.Status == "ACTIVE" &&
        x.UserRoleUsers.Any(r => r.Role.Code == "ADMIN" || r.Role.Code == "DEPARTMENT_STAFF" || r.Role.Code == "LECTURER"), ct);
    private static bool Covers(GateReviewerAssignment assignment, ProjectGateRequirement requirement) =>
        CoversRequirement(assignment, requirement);

    private async Task RequireScopeMutationAuthority(ProjectAcademicScope scope, string normalizedScope, CancellationToken ct)
    {
        if (await IsAdmin(ct)) return;
        var actorDepartment = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
        if (!actorDepartment.HasValue) throw new ForbiddenException("The account has no academic department scope.");
        if (normalizedScope == "COMMON")
        {
            if (scope.LeadDepartmentId != actorDepartment) throw new ForbiddenException("Only the lead department may mutate COMMON review scope.");
            return;
        }
        if (!long.TryParse(normalizedScope, out var majorId)) throw new ForbiddenException("The review scope is invalid.");
        var departmentId = await db.Majors.Where(x => x.Id == majorId).Select(x => (long?)x.DepartmentId).SingleOrDefaultAsync(ct);
        if (departmentId != actorDepartment) throw new ForbiddenException("The account is outside the requested major scope.");
    }

    private async Task RequireGateMutationAuthority(ProjectAcademicScope scope, ProjectGateInstance gate, CancellationToken ct)
    {
        if (await IsAdmin(ct)) return;
        var scopedMajors = gate.Requirements.Where(x => x.Scope == "MAJOR_SPECIFIC" && x.MajorId.HasValue).Select(x => x.MajorId!.Value).Distinct().ToArray();
        if (scopedMajors.Length > 0)
        {
            var actorDepartment = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
            var allowed = await db.Majors.AnyAsync(x => scopedMajors.Contains(x.Id) && x.DepartmentId == actorDepartment, ct);
            if (!allowed) throw new ForbiddenException("The account is outside the gate's major scope.");
            return;
        }
        await RequireScopeMutationAuthority(scope, "COMMON", ct);
    }

    private async Task ReopenAssignmentsForRevision(ProjectGateInstance gate, CancellationToken ct)
    {
        var assignments = await db.GateReviewerAssignments.Where(x => x.GateInstanceId == gate.Id && x.Status != "REVOKED").ToListAsync(ct);
        foreach (var assignment in assignments)
        {
            assignment.Status = "PENDING_REVIEW";
            assignment.LatestDecision = null;
            assignment.ReviewerNotes = null;
            assignment.ConcurrencyToken = Guid.NewGuid();
        }
        if (assignments.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task OpenNextGate(ProjectGateInstance gate, CancellationToken ct)
    {
        var next = await db.ProjectGateInstances.Where(x => x.ProjectId == gate.ProjectId && x.Sequence > gate.Sequence)
            .Where(x => x.Status == "LOCKED" || x.ReviewEligibility == "BLOCKED")
            .OrderBy(x => x.Sequence).FirstOrDefaultAsync(ct);
        if (next is null) return;
        next.Status = "PLANNED";
        next.ReviewEligibility = "READY";
        next.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }
    private static long ParseId(string value, string field) => long.TryParse(value, out var id) && id > 0 ? id : throw new ValidationException(new Dictionary<string, string[]> { [field] = ["A positive numeric id is required."] });

    private async Task RequireProjectRead(long projectId, ProjectAcademicScope scope, CancellationToken ct)
    {
        if (await CanReadProjectScope(projectId, scope, ct)) return;
        throw new ForbiddenException("The account is outside the project academic scope.");
    }
    private async Task<bool> CanReadProjectScope(long projectId, ProjectAcademicScope scope, CancellationToken ct)
    {
        if (await IsAdmin(ct)) return true;
        if (await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.LeftAt == null, ct)) return true;
        if (await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == projectId && x.EndedAt == null && x.SupervisorProfile.UserId == Actor, ct)) return true;
        var now = Now;
        if (await db.GateReviewerAssignments.AnyAsync(x => x.GateInstance.ProjectId == projectId && x.ReviewerId == Actor
            && x.Status != "REVOKED"
            && (x.ValidFrom == null || x.ValidFrom <= now)
            && (x.ValidUntil == null || now <= x.ValidUntil), ct)) return true;
        var actorDept = await db.Users.Where(x => x.Id == Actor).Select(x => x.DepartmentId).SingleOrDefaultAsync(ct);
        return await IsStaff(ct) && actorDept.HasValue && scope.DepartmentIds.Contains(actorDept.Value);
    }
    private async Task RequireProjectLeader(long projectId, CancellationToken ct)
    { if (!await db.TeamMembers.AnyAsync(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.IsLeader && x.LeftAt == null, ct)) throw new ForbiddenException("Only the active project leader may submit evidence."); }

    private async Task RequireEvidenceDraftUploader(long projectId, ProjectGateRequirement requirement, CancellationToken ct)
    {
        var member = await db.TeamMembers.AsNoTracking()
            .Where(x => x.Team.Project != null && x.Team.Project.Id == projectId && x.UserId == Actor && x.LeftAt == null)
            .Select(x => new { x.IsLeader, x.User.MajorId })
            .FirstOrDefaultAsync(ct)
            ?? throw new ForbiddenException("Only active team members may upload draft gate evidence.");

        if (requirement.Scope == "MAJOR_SPECIFIC")
        {
            if (member.IsLeader) return;
            var frozenMajor = await FrozenMemberMajor(projectId, Actor, ct) ?? member.MajorId;
            if (requirement.MajorId.HasValue && frozenMajor == requirement.MajorId.Value) return;
            throw new ForbiddenException("Only the project leader or a team member belonging to the target major may upload MAJOR_SPECIFIC draft evidence.");
        }

        if (requirement.Scope == "INDIVIDUAL")
        {
            if (requirement.TargetStudentId == Actor) return;
            throw new ForbiddenException("Only the target student may upload INDIVIDUAL gate evidence.");
        }

        if (!member.IsLeader)
            throw new ForbiddenException("Only the active project leader may upload COMMON or INTEGRATION gate evidence.");
    }

    private static ReviewGateTemplateDto Map(ReviewGateTemplate x) => new(x.Id.ToString(), x.ProjectPeriodId, x.Code, x.Name, x.Mode, x.Major, x.Status, x.Version.ToString("0.##"), x.ConcurrencyToken.ToString("N"), x.Gates.Count, x.Gates.OrderBy(g => g.Sequence).Select(g => new ReviewGateDefinitionDto(g.Id.ToString(), g.Code, g.Sequence, g.Title, g.Objective, g.Required, g.BlockNextGate, g.BlocksDefense, g.WindowStart, g.WindowEnd, g.WindowStart, g.WindowEnd, g.ReviewerPolicy, g.ReviewScope, g.Requirements.OrderBy(r => r.Id).Select(r => new ReviewGateRequirementDto(r.Id.ToString(), r.Code, r.Title, r.Description, r.Type, r.Scope, r.MajorId, r.TargetStudentId, r.Verification, r.Required)).ToArray(), g.Weight)).ToArray());
    private static AssignedReviewItemDto AssignmentDto(GateReviewerAssignment x, AIPMS.Infrastructure.Persistence.Generated.Models.Project project, IReadOnlyDictionary<long, string>? submitterNames = null, long? latestPackageId = null) => new(x.Id.ToString(), project.Id.ToString(), project.Code, project.Title, x.GateInstance.Code, x.GateInstance.Title, x.Scope, x.GateInstance.WindowEnd, x.Status, x.LatestDecision,
        x.GateInstance.Requirements.Where(r => CoversRequirement(x, r))
            .Select(r => new StudentGateRequirementDto(r.Id.ToString(), r.Code, r.Title, r.Type, r.Scope, r.Required, r.VerificationStatus, Artifact(r, x.GateInstance.Code, submitterNames), r.ReviewerFeedback, r.MajorId, r.TargetStudentId)).ToArray(),
        x.ReviewerNotes, x.ConcurrencyToken.ToString("N"), LatestPackageId: latestPackageId);
    private async Task<ProjectReviewMatrixRowDto> MatrixRow(long projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Include(x => x.ProjectMajors).ThenInclude(x => x.Major).ThenInclude(x => x.Department).SingleAsync(x => x.Id == projectId, ct);
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(x => x.ProjectId == projectId).OrderBy(x => x.Sequence).ToArrayAsync(ct);
        var scopes = await db.ProjectGateScopeDecisions.AsNoTracking().Where(x => x.ProjectId == projectId).ToDictionaryAsync(x => x.Scope, x => x.Status, ct);
        var majors = project.ProjectMajors.Select(x => x.Major.Name).ToArray();
        var taskCount = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == projectId && x.Status != "CANCELLED", ct);
        var doneCount = await db.Tasks.CountAsync(x => x.Milestone.ProjectId == projectId && (x.Status == "DONE" || x.Status == "COMPLETED"), ct);
        var hasTasks = taskCount > 0;
        var taskProgress = hasTasks ? Math.Round(doneCount * 100m / taskCount, 2) : 0m;
        var blocking = gates.ToDictionary(x => x.Code,
            x => (IReadOnlyList<string>)Blocking(x).Select(reason => reason.Message).ToArray());
        var labels = scopes.Keys.ToDictionary(scope => scope, scope => scope == "COMMON"
            ? "Tích hợp chung"
            : project.ProjectMajors.FirstOrDefault(x => x.MajorId.ToString() == scope)?.Major.Name ?? $"Ngành {scope}");
        return new(project.Id.ToString(), project.Title, majors.Length > 1 ? "INTERDISCIPLINARY" : "SINGLE_MAJOR", string.Join(" + ", majors), string.Join(" + ", project.ProjectMajors.Select(x => x.Major.Department.Name).Distinct()), taskProgress, gates.ToDictionary(x => x.Code, x => x.Status), blocking, scopes, labels, hasTasks,
            gates.ToDictionary(x => x.Code, x => x.DecisionSource), project.Code);
    }
}
