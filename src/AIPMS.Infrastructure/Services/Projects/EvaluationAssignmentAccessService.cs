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
using AIPMS.Infrastructure.Persistence.Generated.Models;
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

    private async Task<(FinalSubmission? Final, IReadOnlyList<InternalEvidenceItem> AllItems, IReadOnlyList<EvaluationAssignmentEvidenceItemDto> ScopedItems)> ResolveEvidenceItemsAsync(EvaluationAssignment row, CancellationToken ct)
    {
        var final = await db.Set<FinalSubmission>().AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.ProjectId == row.ProjectId)
            .OrderByDescending(x => x.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        var projectEvidences = await db.Set<ProjectEvidence>().AsNoTracking()
            .Where(pe => pe.ProjectId == row.ProjectId)
            .ToListAsync(ct);

        var deliverableIds = final?.Items.Select(i => i.DeliverableId).Distinct().ToList() ?? new List<long>();
        var deliverables = deliverableIds.Count > 0
            ? await db.Deliverables.AsNoTracking().Where(d => deliverableIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct)
            : new Dictionary<long, Deliverable>();

        var userIds = new HashSet<long>();
        if (final != null)
        {
            userIds.Add(final.SubmittedBy);
            foreach (var del in deliverables.Values)
                userIds.Add(del.CreatedBy);
        }
        foreach (var pe in projectEvidences)
            userIds.Add(pe.SubmittedBy);

        var users = userIds.Count > 0
            ? await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct)
            : new Dictionary<long, User>();

        var rawItems = new List<InternalEvidenceItem>();

        if (final != null)
        {
            foreach (var item in final.Items)
            {
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

                deliverables.TryGetValue(item.DeliverableId, out var del);
                var matchingPe = projectEvidences.FirstOrDefault(pe => pe.DeliverableId == item.DeliverableId || pe.SourceId == item.DeliverableId);

                if (snapshotFiles != null && snapshotFiles.Length > 0)
                {
                    foreach (var f in snapshotFiles)
                    {
                        var studentId = f.Metadata.UploadedBy > 0 ? f.Metadata.UploadedBy : (del?.CreatedBy ?? final.SubmittedBy);
                        long? majorId = matchingPe?.MajorId;
                        if (!majorId.HasValue && users.TryGetValue(studentId, out var submitterUser))
                        {
                            majorId = submitterUser.MajorId;
                        }

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
                    var studentId = del?.CreatedBy ?? final.SubmittedBy;
                    long? majorId = matchingPe?.MajorId;
                    if (!majorId.HasValue && users.TryGetValue(studentId, out var submitterUser))
                    {
                        majorId = submitterUser.MajorId;
                    }

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
        }

        // Also include standalone ProjectEvidence if not already covered
        foreach (var pe in projectEvidences)
        {
            if (pe.DeliverableId.HasValue && rawItems.Any(r => r.Dto.SourceId == pe.DeliverableId.Value))
                continue;

            long? majorId = pe.MajorId;
            if (!majorId.HasValue && users.TryGetValue(pe.SubmittedBy, out var submitterUser))
                majorId = submitterUser.MajorId;

            string? fileName = null;
            string? contentType = null;
            long? fileSize = null;
            string? storageKey = null;

            if (pe.FileId.HasValue)
            {
                var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == pe.FileId.Value && f.DeliverableVersionId == null, ct);
                if (file != null)
                {
                    fileName = file.OriginalFileName;
                    contentType = file.MimeType;
                    fileSize = file.FileSizeBytes;
                    storageKey = file.StoragePath;
                }
            }

            var dto = new EvaluationAssignmentEvidenceItemDto(
                pe.Id,
                pe.Notes ?? pe.SourceType,
                pe.Notes,
                pe.SourceType,
                pe.SourceId,
                pe.FileId,
                fileName,
                contentType,
                fileSize,
                majorId,
                pe.SubmittedBy,
                pe.SubmittedAt,
                pe.FileId.HasValue ? $"/api/v1/evaluation-assignments/{row.Id}/evidence/files/{pe.FileId.Value}" : null);

            rawItems.Add(new(dto, storageKey));
        }

        // Apply assignment scope filtering
        var scopedItems = rawItems.Select(r => r.Dto).ToList();

        if (row.Scope == "MAJOR_SPECIFIC" && row.MajorId.HasValue)
        {
            scopedItems = scopedItems.Where(i => i.MajorId == row.MajorId.Value).ToList();
        }
        else if (row.Scope == "INDIVIDUAL" && row.StudentId.HasValue)
        {
            scopedItems = scopedItems.Where(i => i.StudentId == row.StudentId.Value).ToList();
        }

        return (final, rawItems, scopedItems);
    }

    public async Task<EvaluationAssignmentEvidenceDto> EvidenceAsync(long assignmentId, CancellationToken ct = default)
    {
        var (row, _) = await Load(assignmentId, ct);
        var (final, _, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);
        return new(row.Id, row.ProjectId, row.Scope, row.MajorId, row.StudentId, final?.Id, final?.SubmittedAt, scopedItems.Count, true, scopedItems);
    }

    public async Task<FileDownload> DownloadEvidenceFileAsync(long assignmentId, long fileId, CancellationToken ct = default)
    {
        var (row, _) = await Load(assignmentId, ct);
        var (_, allItems, scopedItems) = await ResolveEvidenceItemsAsync(row, ct);

        var scopedMatch = scopedItems.FirstOrDefault(i => i.FileId == fileId);
        if (scopedMatch == null)
        {
            // Check if file belongs to the project at all
            var inAllProjectItems = allItems.Any(i => i.Dto.FileId == fileId);
            if (inAllProjectItems)
            {
                // File belongs to project but outside the scope of THIS assignment
                throw new ForbiddenException("The requested file is outside the scope of this evaluation assignment.");
            }

            throw new NotFoundException("File", fileId);
        }

        var internalItem = allItems.FirstOrDefault(i => i.Dto.FileId == fileId);
        var storageKey = internalItem?.StorageKey;
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file == null) throw new NotFoundException("File", fileId);
            storageKey = file.StoragePath;
        }

        try
        {
            var stream = await storage.OpenReadAsync(storageKey, ct);
            return new FileDownload(stream, scopedMatch.ContentType ?? "application/octet-stream", scopedMatch.FileName ?? $"evidence_{fileId}");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new NotFoundException("File content", fileId);
        }
    }
}
