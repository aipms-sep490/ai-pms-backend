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
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class EvaluationAssignmentAccessService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IFileStorage storage) : IEvaluationAssignmentAccessService
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

    private async Task<(FinalSubmission? Final, IReadOnlyList<InternalEvidenceItem> AllItems, IReadOnlyList<InternalEvidenceItem> ScopedItems)> ResolveEvidenceItemsAsync(EvaluationAssignment row, CancellationToken ct)
    {
        var final = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.ProjectId == row.ProjectId)
            .OrderByDescending(x => x.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (final == null)
        {
            return (null, [], []);
        }

        // NOTE — CASE B: The typed frozen payload (FinalSnapshotFile / ProjectFileDto) contains NO structured
        // TargetStudentId or TargetMajorId fields. Therefore item-level student and major attribution cannot be
        // proven from the frozen package alone.
        //
        // Per security policy:
        //   COMMON     — all items from the locked final submission package are visible.
        //   INDIVIDUAL — zero items visible (no provenance to prove item-level student ownership).
        //   MAJOR_SPECIFIC — zero items visible (no provenance to prove item-level major ownership).
        //
        // Free-text fields (Deliverable.Title, Deliverable.Description, FinalSubmission.Notes),
        // Deliverable.CreatedBy, and file UploadedBy are NEVER used for attribution.
        // Only structured typed data from the frozen snapshot is authoritative.

        var rawItems = new List<InternalEvidenceItem>();

        foreach (var item in final.Items)
        {
            // CASE B: no structured target provenance in typed payload — always null
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
                        majorId,
                        studentId,
                        f.Metadata.CreatedAt != default ? f.Metadata.CreatedAt : final.SubmittedAt,
                        $"/api/v1/evaluation-assignments/{row.Id}/evidence/files/{f.Metadata.Id}");

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
                    null);

                rawItems.Add(new(dto, null));
            }
        }

        // Apply assignment scope filtering:
        // COMMON      — all items from locked frozen package.
        // MAJOR_SPECIFIC / INDIVIDUAL — fail closed (0 items) because typed payload carries no item-level provenance.
        // UNKNOWN / legacy — fail closed (0 items).
        List<InternalEvidenceItem> scopedItems;
        switch (row.Scope)
        {
            case "COMMON":
                scopedItems = rawItems;
                break;
            case "MAJOR_SPECIFIC" when row.MajorId.HasValue && row.MajorId.Value > 0:
                // Item-level majorId is always null (CASE B) → filter produces 0 items. Fail closed.
                scopedItems = rawItems.Where(i => i.Dto.MajorId == row.MajorId.Value).ToList();
                break;
            case "INDIVIDUAL" when row.StudentId.HasValue && row.StudentId.Value > 0:
                // Item-level studentId is always null (CASE B) → filter produces 0 items. Fail closed.
                scopedItems = rawItems.Where(i => i.Dto.StudentId == row.StudentId.Value).ToList();
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
            var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file == null) throw new NotFoundException("File", fileId);
            storageKey = file.StoragePath;
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
