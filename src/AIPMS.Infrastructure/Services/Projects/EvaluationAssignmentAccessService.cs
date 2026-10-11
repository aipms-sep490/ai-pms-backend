using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Abstractions.Storage;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class EvaluationAssignmentAccessService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage,
    IEvaluationDraftRepository evaluations) : IEvaluationAssignmentAccessService
{
    private sealed record InternalEvidenceItem(EvaluationAssignmentEvidenceItemDto Dto, string? StorageKey);

    private async Task<(EvaluationAssignment Row, bool CanScore)> Load(long id, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var row = await db.Set<EvaluationAssignment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("EvaluationAssignment", id);
        var account = await db.Users.AsNoTracking().Where(u => u.Id == actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"), Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), Lecturer = u.UserRoleUsers.Any(r => r.Role.Code == "LECTURER") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException("An active account is required.");
        var academicActor = await evaluations.GetActorAsync(actor, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(row.ProjectId, ct) ?? throw new NotFoundException("Project", row.ProjectId);
        var manager = account.Admin || AcademicAuthority.ManagesComponent(academicActor, project, row.DepartmentId);
        var evaluator = account.Lecturer && row.EvaluatorId == actor && row.Status == "ACTIVE" && account.DepartmentId == row.DepartmentId
            && academicActor.IsLecturer && project.ActiveScope
            && (row.EvaluationType != "SUPERVISOR" || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == row.ProjectId && a.SupervisorProfile.UserId == actor && a.IsPrimary && a.EndedAt == null, ct));
        if (!manager && !evaluator) throw new ForbiddenException("The assignment is outside your scope.");
        return (row, evaluator && project.Status == "FINAL_SUBMISSION" && row.Scope is "COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
    }

    private async Task ValidateEvidenceAssignment(EvaluationAssignment row, CancellationToken ct)
    {
        if (row.Status != "ACTIVE") throw new ForbiddenException("Revoked assignments cannot access evidence.");
        if (row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")) return;
        var component = await db.Set<EvaluationSchemeComponent>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == row.ComponentId, ct);
        var scheme = component is null ? null : await db.Set<EvaluationScheme>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == component.SchemeId, ct);
        if (component is null || scheme is null || scheme.Status != "PUBLISHED" || scheme.ProjectId != row.ProjectId
            || scheme.ProjectPeriodId != row.ProjectPeriodId || scheme.PolicyVersionId != row.PolicyVersionId
            || component.Scope != row.Scope || component.MajorId != row.MajorId || component.RubricId != row.RubricId
            || !await db.Rubrics.AnyAsync(r => r.Id == component.RubricId && r.DepartmentId == row.DepartmentId, ct)
            || row.Scope == "COMMON" && (row.MajorId is not null || row.StudentId is not null)
            || row.Scope == "MAJOR_SPECIFIC" && (row.MajorId is null || row.StudentId is not null)
            || row.Scope == "INDIVIDUAL" && (row.StudentId is null || row.MajorId is null
                || !(JsonSerializer.Deserialize<SchemeStudent[]>(scheme.StudentsJson) ?? [])
                    .Any(s => s.StudentId == row.StudentId && s.MajorId == row.MajorId && s.DepartmentId == row.DepartmentId)))
            throw new ForbiddenException("Assignment does not match its published component and frozen target.");
    }

    public async Task<EvaluationAssignmentDetailDto> GetAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, canScore) = await Load(assignmentId, ct);
        var dto = new EvaluationAssignmentDto(row.Id, row.ProjectId, row.EvaluatorId, row.RubricId, row.ProjectPeriodId, row.DepartmentId,
            row.EvaluationType, row.Status, row.AssignedBy, row.AssignedAt, row.RevokedAt, row.ConcurrencyToken.ToString("N"), row.Scope, row.MajorId, row.StudentId, row.ComponentId, row.PolicyVersionId);
        var legacy = row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL");
        return new(dto, canScore && !legacy, legacy, legacy ? "LEGACY_SCOPE_UNKNOWN" : null);
    }

    private async Task<(FinalSubmission? Final, IReadOnlyList<InternalEvidenceItem> AllItems, IReadOnlyList<InternalEvidenceItem> ScopedItems)> ResolveEvidenceItemsAsync(EvaluationAssignment row, CancellationToken ct)
    {
        await ValidateEvidenceAssignment(row, ct);
        var final = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.ProjectId == row.ProjectId)
            .SingleOrDefaultAsync(ct);

        if (final == null)
        {
            return (null, [], []);
        }

        if (!string.IsNullOrWhiteSpace(row.ScopeSnapshotJson))
        {
            using var binding = JsonDocument.Parse(row.ScopeSnapshotJson);
            if (binding.RootElement.TryGetProperty("FinalSubmissionId", out var pinned)
                && (!pinned.TryGetInt64(out var pinnedId) || pinnedId != final.Id))
                throw new ForbiddenException("The locked package differs from this assignment's package.");
        }

        // Targets and object keys come only from the locked snapshot.
        var rawItems = new List<InternalEvidenceItem>();

        foreach (var item in final.Items)
        {
            long? studentId = null;
            long? majorId = null;

            FinalSnapshotFile[]? snapshotFiles = null;
            if (!string.IsNullOrWhiteSpace(item.FilesJson))
            {
                try
                {
                    snapshotFiles = JsonSerializer.Deserialize<FinalSnapshotFile[]>(item.FilesJson);
                }
                catch (JsonException)
                {
                    snapshotFiles = null;
                }
            }

            if (snapshotFiles != null && snapshotFiles.Length > 0)
            {
                foreach (var f in snapshotFiles)
                {
                    if (f?.Metadata is null || f.Metadata.ParentType is not ("VERSION" or "DELIVERABLE_VERSION")
                        || f.Metadata.ParentId != item.DeliverableVersionId)
                        continue;
                    var dto = new EvaluationAssignmentEvidenceItemDto(
                        f.Metadata.Id,
                        item.Title,
                        $"Version {item.VersionNumber} - {item.StatusAtSubmission}",
                        "DELIVERABLE",
                        item.DeliverableId,
                        f.Metadata.Id,
                        f.Metadata.FileName,
                        f.Metadata.ContentType,
                        f.Metadata.SizeBytes,
                        f.EvidenceScope is "MAJOR_SPECIFIC" or "INDIVIDUAL" ? f.TargetMajorId : null,
                        f.EvidenceScope == "INDIVIDUAL" ? f.TargetStudentId : null,
                        f.Metadata.CreatedAt != default ? f.Metadata.CreatedAt : final.SubmittedAt,
                        $"/api/v1/evaluation-assignments/{row.Id}/evidence/files/{f.Metadata.Id}",
                        item.DeliverableVersionId, item.VersionNumber, f.Metadata.Sha256);

                    rawItems.Add(new(dto, f.StorageKey));
                }
            }
            else
            {
                var dto = new EvaluationAssignmentEvidenceItemDto(
                    item.DeliverableVersionId,
                    item.Title,
                    $"Version {item.VersionNumber} - {item.StatusAtSubmission}",
                    "DELIVERABLE",
                    item.DeliverableId,
                    null,
                    null,
                    null,
                    null,
                    majorId,
                    studentId,
                    final.SubmittedAt,
                    null, item.DeliverableVersionId, item.VersionNumber);

                rawItems.Add(new(dto, null));
            }
        }

        // Apply assignment scope filtering:
        // COMMON      — all items from locked frozen package.
        // UNKNOWN / legacy — fail closed (0 items).
        List<InternalEvidenceItem> scopedItems;
        switch (row.Scope)
        {
            case "COMMON":
                scopedItems = rawItems;
                break;
            case "MAJOR_SPECIFIC" when row.MajorId.HasValue && row.MajorId.Value > 0:
                scopedItems = rawItems.Where(i => i.Dto.MajorId == row.MajorId.Value).ToList();
                break;
            case "INDIVIDUAL" when row.StudentId.HasValue && row.StudentId.Value > 0:
                scopedItems = rawItems.Where(i => i.Dto.StudentId == row.StudentId.Value && i.Dto.MajorId == row.MajorId).ToList();
                break;
            default:
                scopedItems = [];
                break;
        }

        return (final, rawItems, scopedItems);
    }

    public async Task<EvaluationAssignmentEvidenceDto> EvidenceAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, _) = await Load(assignmentId, ct);
        var (final, _, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);
        var dtos = scopedItems.Select(x => x.Dto).ToList();
        return new(row.Id, row.ProjectId, row.Scope, row.MajorId, row.StudentId, final?.Id, final?.SubmittedAt, dtos.Count, true, dtos);
    }

    public async Task<FileDownload> DownloadEvidenceFileAsync(long assignmentId, long fileId, CancellationToken ct = default)
    {
        var (row, _) = await Load(assignmentId, ct);
        if (row.Scope is not ("COMMON" or "MAJOR_SPECIFIC" or "INDIVIDUAL")
            || (row.Scope == "MAJOR_SPECIFIC" && (!row.MajorId.HasValue || row.MajorId.Value <= 0))
            || (row.Scope == "INDIVIDUAL" && (!row.StudentId.HasValue || row.StudentId.Value <= 0)))
        {
            throw new ForbiddenException("Legacy or unknown evaluation assignment scope cannot access evidence files.");
        }

        var (_, allItems, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);

        var scopedMatch = scopedItems.FirstOrDefault(i => i.Dto.FileId == fileId);
        if (scopedMatch == null)
        {
            // Check if file belongs to the locked final submission package at all
            var inAllProjectItems = allItems.Any(i => i.Dto.FileId == fileId);
            if (inAllProjectItems)
            {
                // File belongs to project locked submission but outside the scope of THIS assignment
                throw new ForbiddenException("The requested file is outside the scope of this evaluation assignment.");
            }

            throw new NotFoundException("File", fileId);
        }

        var storageKey = scopedMatch.StorageKey;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ConflictException("The locked file has no verifiable storage reference.");
        }

        try
        {
            var stream = await storage.OpenReadAsync(storageKey, ct);
            return new FileDownload(stream, scopedMatch.Dto.ContentType ?? "application/octet-stream", scopedMatch.Dto.FileName ?? $"evidence_{fileId}");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new NotFoundException("File content", fileId);
        }
    }
}
