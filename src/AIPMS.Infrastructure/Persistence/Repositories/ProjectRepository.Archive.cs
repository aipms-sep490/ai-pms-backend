using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Domain.Exceptions;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Services.Projects;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed partial class ProjectRepository
{
    public async Task<long?> GetLeadDepartmentIdAsync(long projectId, CancellationToken cancellationToken)
    {
        var status = await context.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null) return null;

        if (status is not ("DRAFT" or "REVISION_REQUIRED"))
        {
            var snapshot = await LatestRegistrationAsync(projectId, cancellationToken);
            if (snapshot is not null && snapshot.LeadDepartmentId > 0)
                return snapshot.LeadDepartmentId;
        }

        var teamId = await context.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => p.TeamId)
            .SingleOrDefaultAsync(cancellationToken);

        if (teamId > 0)
        {
            var configLead = await context.Set<TeamAcademicConfiguration>().AsNoTracking()
                .Where(c => c.TeamId == teamId)
                .Select(c => (long?)c.LeadDepartmentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (configLead is > 0)
                return configLead;
        }

        return await context.ProjectMajors.AsNoTracking()
            .Where(pm => pm.ProjectId == projectId)
            .OrderBy(pm => pm.MajorId)
            .Select(pm => (long?)pm.Major.DepartmentId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlockingRequirement>> GetArchiveBlockersAsync(
        long projectId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var blockers = new List<BlockingRequirement>();

        var hasProjectResult = await context.Set<ProjectResult>().AsNoTracking()
            .AnyAsync(r => r.ProjectId == projectId, cancellationToken);

        var activeStudentIds = await context.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .SelectMany(p => p.Team.TeamMembers.Where(tm => tm.LeftAt == null).Select(tm => tm.UserId))
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var publishedStudentIds = await context.Set<StudentResult>().AsNoTracking()
            .Where(sr => sr.ProjectId == projectId)
            .Select(sr => sr.StudentId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (!hasProjectResult || activeStudentIds.Any(id => !publishedStudentIds.Contains(id)))
        {
            blockers.Add(new BlockingRequirement(
                "RESULTS_NOT_PUBLISHED",
                "Chưa công bố đầy đủ kết quả đồ án và kết quả từng sinh viên."));
        }

        var hasOpenWindow = await context.Set<ResultAppealWindow>().AsNoTracking()
            .AnyAsync(w => w.ProjectId == projectId && w.Status == "OPEN" && w.ClosesAt > utcNow, cancellationToken);
        if (hasOpenWindow)
        {
            blockers.Add(new BlockingRequirement(
                "APPEAL_WINDOWS_OPEN",
                "Vẫn còn cửa sổ phúc khảo đang mở."));
        }

        var hasOpenAppeals = await context.Set<ResultAppealRequest>().AsNoTracking()
            .AnyAsync(r => r.ProjectId == projectId
                && r.Status != "ACCEPTED"
                && r.Status != "REJECTED"
                && r.Status != "WITHDRAWN", cancellationToken);
        if (hasOpenAppeals)
        {
            blockers.Add(new BlockingRequirement(
                "OPEN_APPEALS_EXIST",
                "Vẫn còn đơn phúc khảo hoặc đính chính đang chờ xử lý."));
        }

        return blockers;
    }

    public async Task<ProjectArchiveDossierDto?> GetArchiveDossierAsync(
        long projectId,
        long actorUserId,
        bool isAdmin,
        long? staffDepartmentId,
        bool isLeadStaff,
        CancellationToken cancellationToken)
    {
        var projectDto = await GetByIdAsync(projectId, cancellationToken);
        if (projectDto is null) return null;

        var projectMeta = await context.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.TeamId,
                SemesterCode = p.Team.AcademicSemester.Code,
                SemesterName = p.Team.AcademicSemester.Name
            })
            .SingleAsync(cancellationToken);

        var leadDepartmentId = await GetLeadDepartmentIdAsync(projectId, cancellationToken);
        var projectDeptIds = await GetProjectMajorDepartmentIdsAsync(projectId, cancellationToken);
        var deptLookupIds = projectDeptIds.Concat(leadDepartmentId.HasValue ? [leadDepartmentId.Value] : []).Distinct().ToArray();
        var deptNames = deptLookupIds.Length == 0
            ? new Dictionary<long, string>()
            : await context.Departments.AsNoTracking()
                .Where(d => deptLookupIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

        var leadDeptName = leadDepartmentId.HasValue ? deptNames.GetValueOrDefault(leadDepartmentId.Value) : null;
        var participatingDeptNames = projectDeptIds.Distinct()
            .Select(id => deptNames.GetValueOrDefault(id))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Cast<string>()
            .ToArray();

        var archivedAt = await context.ProjectStatusHistories.AsNoTracking()
            .Where(h => h.ProjectId == projectId && h.NewStatus == "ARCHIVED")
            .OrderByDescending(h => h.Id)
            .Select(h => (DateTime?)DateTime.SpecifyKind(h.ChangedAt, DateTimeKind.Utc))
            .FirstOrDefaultAsync(cancellationToken);

        var majorsMap = await context.Majors.AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        var rawMembers = await context.TeamMembers.AsNoTracking()
            .Where(tm => tm.TeamId == projectMeta.TeamId && tm.LeftAt == null)
            .OrderByDescending(tm => tm.IsLeader)
            .ThenBy(tm => tm.Id)
            .Select(tm => new
            {
                tm.UserId,
                tm.IsLeader
            })
            .ToListAsync(cancellationToken);

        var rawSupervisors = await context.SupervisorAssignments.AsNoTracking()
            .Where(sa => sa.ProjectId == projectId)
            .OrderByDescending(sa => sa.IsPrimary)
            .ThenBy(sa => sa.EndedAt != null)
            .ThenBy(sa => sa.AssignedAt)
            .Select(sa => new
            {
                UserId = sa.SupervisorProfile.UserId,
                sa.AssignmentType,
                sa.IsPrimary,
                sa.MajorId,
                IsActive = sa.EndedAt == null,
                sa.AssignedAt,
                sa.EndedAt
            })
            .ToListAsync(cancellationToken);

        var committees = await context.Set<DefenseCommittee>().AsNoTracking()
            .Include(c => c.Members)
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.Status == "CONFIRMED")
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);
        var committee = committees.FirstOrDefault();

        var sessions = await context.Set<DefenseSession>().AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Status != "CANCELLED")
            .ThenByDescending(s => s.Id)
            .ToListAsync(cancellationToken);
        var session = sessions.FirstOrDefault();

        var finalSubmissionEntity = await context.Set<FinalSubmission>().AsNoTracking()
            .Include(fs => fs.Items)
            .SingleOrDefaultAsync(fs => fs.ProjectId == projectId, cancellationToken);

        var appealRows = await context.Set<ResultAppealRequest>().AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.RequestedAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);

        var revisionRows = await context.Set<ResultRevision>().AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.Version)
            .ThenBy(r => r.TargetKey)
            .ToListAsync(cancellationToken);

        var projectResult = await PublishedResultResolver.Project(context, projectId, cancellationToken);

        var allStudentResultRows = await context.Set<StudentResult>().AsNoTracking()
            .Where(sr => sr.ProjectId == projectId)
            .OrderBy(sr => sr.StudentId)
            .ToListAsync(cancellationToken);

        var canSeeAllStudentDetails = isAdmin
            || staffDepartmentId.HasValue
            || rawSupervisors.Any(s => s.UserId == actorUserId)
            || (committee is not null && committee.Members.Any(m => m.UserId == actorUserId));

        var studentResults = new List<StudentResultDto>();
        foreach (var sr in allStudentResultRows)
        {
            if (!canSeeAllStudentDetails && sr.StudentId != actorUserId)
                continue;
            var resolved = await PublishedResultResolver.Student(context, projectId, sr.StudentId, cancellationToken);
            if (resolved is not null)
                studentResults.Add(resolved);
        }

        var visibleRevisionRows = canSeeAllStudentDetails
            ? revisionRows
            : revisionRows.Where(r => r.TargetKey == 0 || r.StudentId == actorUserId).ToList();

        var statusHistoryEntities = await context.ProjectStatusHistories.AsNoTracking()
            .Include(h => h.ChangedByNavigation)
            .Where(h => h.ProjectId == projectId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync(cancellationToken);

        var componentIds = appealRows.Select(a => a.ComponentId).Distinct().ToArray();
        var componentNames = componentIds.Length == 0
            ? new Dictionary<long, string>()
            : await context.Set<EvaluationSchemeComponent>().AsNoTracking()
                .Where(c => componentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var lookupUserIds = rawMembers.Select(m => m.UserId)
            .Concat(rawSupervisors.Select(s => s.UserId))
            .Concat(committee?.Members.Select(cm => cm.UserId) ?? [])
            .Concat(visibleRevisionRows.SelectMany(r => r.StudentId.HasValue ? new[] { r.PublishedBy, r.StudentId.Value } : new[] { r.PublishedBy }))
            .Concat(projectResult is not null ? [projectResult.PublishedBy] : [])
            .Concat(finalSubmissionEntity is not null
                ? new[] { finalSubmissionEntity.ForceLockedBy ?? finalSubmissionEntity.LockedBySupervisorId ?? finalSubmissionEntity.SubmittedBy }
                : [])
            .Distinct()
            .ToArray();

        var usersMap = lookupUserIds.Length == 0
            ? new Dictionary<long, (string FullName, string Email, string? StudentCode, long? MajorId)>()
            : (await context.Users.AsNoTracking()
                .Where(u => lookupUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email, u.StudentCode, u.MajorId })
                .ToListAsync(cancellationToken))
                .ToDictionary(u => u.Id, u => (u.FullName, u.Email, u.StudentCode, u.MajorId));

        var memberDtos = rawMembers.Select(m =>
        {
            var info = usersMap.GetValueOrDefault(m.UserId);
            return new ArchiveDossierMemberDto(
                m.UserId,
                info.StudentCode,
                info.FullName ?? $"User #{m.UserId}",
                info.Email ?? string.Empty,
                info.MajorId,
                info.MajorId.HasValue ? majorsMap.GetValueOrDefault(info.MajorId.Value) : null,
                m.IsLeader);
        }).ToArray();

        var supervisorDtos = rawSupervisors.Select(s =>
        {
            var info = usersMap.GetValueOrDefault(s.UserId);
            return new ArchiveDossierSupervisorDto(
                s.UserId,
                info.FullName ?? $"User #{s.UserId}",
                info.Email ?? string.Empty,
                s.AssignmentType,
                s.IsPrimary,
                s.MajorId,
                s.MajorId.HasValue ? majorsMap.GetValueOrDefault(s.MajorId.Value) : null,
                s.IsActive,
                DateTime.SpecifyKind(s.AssignedAt, DateTimeKind.Utc),
                s.EndedAt.HasValue ? DateTime.SpecifyKind(s.EndedAt.Value, DateTimeKind.Utc) : null);
        }).ToArray();

        ArchiveDossierDefenseDto? defenseDto = null;
        if (committee is not null || session is not null)
        {
            var committeeMembers = (committee?.Members ?? [])
                .OrderBy(cm => cm.MajorId)
                .ThenBy(cm => cm.UserId)
                .Select(cm => new ArchiveDossierDefenseCommitteeMemberDto(
                    cm.UserId,
                    usersMap.GetValueOrDefault(cm.UserId).FullName ?? $"User #{cm.UserId}",
                    cm.MajorId,
                    majorsMap.GetValueOrDefault(cm.MajorId),
                    cm.Role))
                .ToArray();

            defenseDto = new ArchiveDossierDefenseDto(
                committee?.Name,
                committee?.Status,
                session?.Room,
                session?.StartAt is DateTime startAt ? DateTime.SpecifyKind(startAt, DateTimeKind.Utc) : null,
                session?.EndAt is DateTime endAt ? DateTime.SpecifyKind(endAt, DateTimeKind.Utc) : null,
                committeeMembers);
        }

        ArchiveDossierFinalSubmissionDto? finalDto = null;
        if (finalSubmissionEntity is not null)
        {
            var lockedById = finalSubmissionEntity.ForceLockedBy ?? finalSubmissionEntity.LockedBySupervisorId ?? finalSubmissionEntity.SubmittedBy;
            var finalItems = finalSubmissionEntity.Items
                .OrderBy(i => i.DeliverableId)
                .ThenBy(i => i.DeliverableVersionId)
                .Select(item =>
                {
                    var files = ParseFinalSnapshotFiles(item.FilesJson);
                    return new ArchiveDossierFinalItemDto(
                        item.DeliverableId,
                        item.Title,
                        item.DeliverableVersionId,
                        files);
                })
                .ToArray();

            finalDto = new ArchiveDossierFinalSubmissionDto(
                finalSubmissionEntity.Id,
                DateTime.SpecifyKind(finalSubmissionEntity.SubmittedAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(finalSubmissionEntity.SubmittedAt, DateTimeKind.Utc),
                usersMap.GetValueOrDefault(lockedById).FullName,
                finalItems);
        }

        var visibleAppeals = canSeeAllStudentDetails
            ? appealRows
            : appealRows.Where(a => a.TargetKey == 0 || a.StudentId == actorUserId || a.RequestedBy == actorUserId).ToList();

        var appealItems = visibleAppeals.Select(a =>
        {
            var targetLabel = a.StudentId.HasValue
                ? (memberDtos.FirstOrDefault(m => m.UserId == a.StudentId.Value)?.FullName ?? $"Sinh viên #{a.StudentId.Value}")
                : "Kết quả đồ án chung";
            return new ArchiveDossierAppealDto(
                a.Id,
                a.Source,
                a.AppealKind,
                targetLabel,
                componentNames.GetValueOrDefault(a.ComponentId),
                a.Status,
                a.ResultVersion,
                DateTime.SpecifyKind(a.RequestedAt, DateTimeKind.Utc),
                a.DecisionReason);
        }).ToArray();

        var appealsSummary = new ArchiveDossierAppealsSummaryDto(
            visibleAppeals.Count,
            visibleAppeals.Count(a => a.Status == "ACCEPTED"),
            visibleAppeals.Count(a => a.Status == "REJECTED"),
            visibleAppeals.Count(a => a.Status == "WITHDRAWN"),
            visibleAppeals.Count(a => a.Status is not ("ACCEPTED" or "REJECTED" or "WITHDRAWN")),
            appealItems);

        var revisionHistory = new List<ResultRevisionSummary>();
        foreach (var r in visibleRevisionRows)
        {
            if (r.TargetKey == 0)
            {
                var pDto = PublishedResultResolver.Project(r);
                revisionHistory.Add(new ResultRevisionSummary(
                    pDto.RevisionId,
                    pDto.Version,
                    pDto.SupersedesVersion,
                    null,
                    pDto.TotalScore,
                    pDto.Outcome,
                    pDto.PublishedBy,
                    DateTime.SpecifyKind(pDto.PublishedAt, DateTimeKind.Utc),
                    null,
                    r.BundleId,
                    usersMap.GetValueOrDefault(pDto.PublishedBy).FullName,
                    null));
            }
            else
            {
                var sDto = PublishedResultResolver.Student(r);
                revisionHistory.Add(new ResultRevisionSummary(
                    sDto.RevisionId,
                    sDto.Version,
                    sDto.SupersedesVersion,
                    sDto.StudentId,
                    sDto.TotalScore,
                    sDto.Outcome,
                    sDto.PublishedBy,
                    DateTime.SpecifyKind(sDto.PublishedAt, DateTimeKind.Utc),
                    null,
                    r.BundleId,
                    usersMap.GetValueOrDefault(sDto.PublishedBy).FullName,
                    usersMap.GetValueOrDefault(sDto.StudentId).FullName));
            }
        }
        if (projectResult is not null && revisionHistory.All(r => !(r.StudentId == null && r.Version == 1)))
        {
            var rootProjectResult = await context.Set<ProjectResult>().AsNoTracking()
                .SingleOrDefaultAsync(pr => pr.ProjectId == projectId, cancellationToken);
            if (rootProjectResult is not null)
            {
                var rootDto = PublishedResultResolver.Root(rootProjectResult);
                revisionHistory.Add(new ResultRevisionSummary(
                    null,
                    1,
                    null,
                    null,
                    rootDto.TotalScore,
                    rootDto.Outcome,
                    rootDto.PublishedBy,
                    DateTime.SpecifyKind(rootDto.PublishedAt, DateTimeKind.Utc),
                    null,
                    null,
                    usersMap.GetValueOrDefault(rootDto.PublishedBy).FullName,
                    null));
            }
        }

        var statusHistoryDtos = statusHistoryEntities.Select(h => new ProjectStatusHistoryDto(
            h.Id,
            h.ProjectId,
            h.OldStatus,
            h.NewStatus,
            h.ChangedBy,
            h.ChangedByNavigation.FullName,
            h.Reason,
            DateTime.SpecifyKind(h.ChangedAt, DateTimeKind.Utc))).ToArray();

        return new ProjectArchiveDossierDto(
            projectDto.Id,
            projectDto.Code,
            projectDto.Title,
            projectDto.Description,
            projectDto.Status,
            projectDto.TrackCode,
            projectMeta.SemesterCode,
            projectMeta.SemesterName,
            leadDepartmentId,
            leadDeptName,
            participatingDeptNames,
            projectDto.Majors,
            DateTime.SpecifyKind(projectDto.RegisteredAt, DateTimeKind.Utc),
            projectDto.SubmittedAt.HasValue ? DateTime.SpecifyKind(projectDto.SubmittedAt.Value, DateTimeKind.Utc) : null,
            projectDto.ApprovedAt.HasValue ? DateTime.SpecifyKind(projectDto.ApprovedAt.Value, DateTimeKind.Utc) : null,
            projectDto.CompletedAt.HasValue ? DateTime.SpecifyKind(projectDto.CompletedAt.Value, DateTimeKind.Utc) : null,
            archivedAt,
            projectDto.ConcurrencyToken,
            isLeadStaff && string.Equals(projectDto.Status, "ARCHIVED", StringComparison.OrdinalIgnoreCase),
            memberDtos,
            supervisorDtos,
            new ArchiveDossierResultsDto(projectResult, studentResults, revisionHistory),
            defenseDto,
            finalDto,
            appealsSummary,
            statusHistoryDtos);
    }

    private static IReadOnlyList<ArchiveDossierFinalFileDto> ParseFinalSnapshotFiles(string filesJson)
    {
        if (string.IsNullOrWhiteSpace(filesJson))
            return [];

        try
        {
            var snapshotFiles = JsonSerializer.Deserialize<FinalSnapshotFile[]>(filesJson);
            if (snapshotFiles is { Length: > 0 })
            {
                return snapshotFiles.Select(f => new ArchiveDossierFinalFileDto(
                    f.Metadata.Id,
                    f.Metadata.FileName,
                    f.Metadata.Sha256,
                    f.Metadata.SizeBytes,
                    $"/api/v1/files/{f.Metadata.Id}/download",
                    f.TargetMajorId,
                    f.EvidenceScope ?? "COMMON")).ToArray();
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return [];
    }
}
