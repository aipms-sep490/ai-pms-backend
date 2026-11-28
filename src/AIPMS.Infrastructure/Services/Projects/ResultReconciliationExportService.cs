using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.Abstractions;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class ResultReconciliationExportService(
    AipmsDbContext db,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider clock) : IResultReconciliationExportService
{
    private const int MaxExportRows = 10000;

    public async Task<RosterFile> ExportDepartmentResultsAsync(
        long? semesterId,
        long? majorId,
        string format,
        string? search,
        CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not long actorId)
            throw new UnauthorizedException();

        var normalizedFormat = (format ?? "xlsx").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("xlsx" or "csv"))
            throw new ValidationException(new Dictionary<string, string[]> { ["format"] = ["Export format must be xlsx or csv."] });

        var actor = await db.Users.AsNoTracking()
            .Where(u => u.Id == actorId && u.Status == "ACTIVE")
            .Select(u => new
            {
                u.DepartmentId,
                IsAdmin = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Admin),
                IsStaff = u.UserRoleUsers.Any(r => r.Role.Code == AppRoles.DepartmentStaff),
                DeptActive = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                OrgId = u.Department != null ? (long?)u.Department.OrganizationId : null
            })
            .SingleOrDefaultAsync(ct) ?? throw new UnauthorizedException();

        if (!actor.IsStaff)
        {
            throw new ForbiddenException(
                actor.IsAdmin
                    ? "Platform administrators without DEPARTMENT_STAFF role cannot export department result reconciliations."
                    : "Only active department staff can export department result reconciliations.",
                actor.IsAdmin ? WorkflowErrorCodes.AdminNotAcademicAuthority : WorkflowErrorCodes.Forbidden);
        }

        if (!actor.DeptActive || !actor.DepartmentId.HasValue || !actor.OrgId.HasValue)
            throw new ForbiddenException("An active department scope is required.", WorkflowErrorCodes.ScopeAccessDenied);

        var deptId = actor.DepartmentId.Value;

        if (semesterId.HasValue && semesterId.Value <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["semesterId"] = ["Semester ID must be positive."] });
        if (majorId.HasValue && majorId.Value <= 0)
            throw new ValidationException(new Dictionary<string, string[]> { ["majorId"] = ["Major ID must be positive."] });

        var effectiveSemesterId = semesterId;
        if (!effectiveSemesterId.HasValue)
        {
            effectiveSemesterId = await db.AcademicSemesters.AsNoTracking()
                .Where(s => s.OrganizationId == actor.OrgId.Value && s.Status == "ACTIVE")
                .OrderByDescending(s => s.StartDate)
                .ThenByDescending(s => s.Id)
                .Select(s => (long?)s.Id)
                .FirstOrDefaultAsync(ct);
        }

        var projectsQuery = db.Projects.AsNoTracking()
            .Where(p => p.ProjectMajors.Any(pm => pm.Major.IsActive
                && pm.Major.DepartmentId == deptId
                && (!majorId.HasValue || pm.MajorId == majorId.Value)));

        if (effectiveSemesterId.HasValue)
            projectsQuery = projectsQuery.Where(p => p.Team.AcademicSemesterId == effectiveSemesterId.Value);

        var projectList = await projectsQuery
            .OrderBy(p => p.Code)
            .ThenBy(p => p.Id)
            .Take(2500)
            .Select(p => new
            {
                p.Id,
                p.TeamId,
                p.Code,
                p.Title,
                p.Status,
                p.TrackCode
            })
            .ToListAsync(ct);

        var projectIds = projectList.Select(p => p.Id).ToArray();
        var teamIds = projectList.Select(p => p.TeamId).Distinct().ToArray();

        var members = teamIds.Length == 0
            ? []
            : await db.TeamMembers.AsNoTracking()
                .Where(tm => teamIds.Contains(tm.TeamId) && tm.LeftAt == null)
                .OrderByDescending(tm => tm.IsLeader)
                .ThenBy(tm => tm.User.StudentCode ?? tm.User.Email)
                .ThenBy(tm => tm.Id)
                .Select(tm => new
                {
                    tm.TeamId,
                    tm.UserId,
                    tm.User.StudentCode,
                    tm.User.FullName,
                    tm.User.Email,
                    tm.User.MajorId,
                    MajorCode = tm.User.Major != null ? tm.User.Major.Code : ""
                })
                .ToListAsync(ct);

        var term = search?.Trim();
        var rows = new List<ResultReconciliationExportRow>();

        if (projectIds.Length > 0)
        {
            var gateInstances = await db.ProjectGateInstances.AsNoTracking()
                .Where(g => projectIds.Contains(g.ProjectId))
                .Select(g => new { g.Id, g.ProjectId, g.Sequence })
                .ToListAsync(ct);
            var gateSequenceById = gateInstances.ToDictionary(g => g.Id, g => g.Sequence);

            var gateScores = await db.Set<GateIndividualProcessScore>().AsNoTracking()
                .Where(s => projectIds.Contains(s.ProjectId) && s.Status == "FINALIZED" && s.Score != null)
                .Select(s => new { s.ProjectId, s.StudentId, s.GateInstanceId, Score = s.Score!.Value, s.GateWeightPercent })
                .ToListAsync(ct);

            var absences = await db.Set<StudentDefenseAbsence>().AsNoTracking()
                .Where(a => projectIds.Contains(a.ProjectId) && a.RevokedAt == null)
                .Select(a => new { a.ProjectId, a.StudentId })
                .ToListAsync(ct);
            var absenceSet = absences.Select(a => (a.ProjectId, a.StudentId)).ToHashSet();

            var appealStats = await db.Set<ResultAppealRequest>().AsNoTracking()
                .Where(a => projectIds.Contains(a.ProjectId))
                .Select(a => new { a.ProjectId, a.TargetKey, a.Status })
                .ToListAsync(ct);

            var studentResultsRaw = await db.Set<StudentResult>().AsNoTracking()
                .Where(r => projectIds.Contains(r.ProjectId))
                .ToListAsync(ct);
            var studentRevisionsRaw = await db.Set<ResultRevision>().AsNoTracking()
                .Where(r => projectIds.Contains(r.ProjectId) && r.TargetKey > 0)
                .OrderByDescending(r => r.Version)
                .ToListAsync(ct);

            var membersByTeam = members.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToArray());

            foreach (var p in projectList)
            {
                if (!membersByTeam.TryGetValue(p.TeamId, out var teamMembers))
                    continue;

                foreach (var m in teamMembers)
                {
                    if (!string.IsNullOrEmpty(term))
                    {
                        var matches = p.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || p.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || (m.StudentCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                            || m.FullName.Contains(term, StringComparison.OrdinalIgnoreCase);
                        if (!matches) continue;
                    }

                    var studentGateRows = gateScores
                        .Where(s => s.ProjectId == p.Id && s.StudentId == m.UserId)
                        .Select(s => new
                        {
                            GateNumber = gateSequenceById.GetValueOrDefault(s.GateInstanceId),
                            s.Score,
                            s.GateWeightPercent
                        })
                        .ToArray();

                    decimal? g1 = studentGateRows.FirstOrDefault(x => x.GateNumber == 1)?.Score;
                    decimal? g2 = studentGateRows.FirstOrDefault(x => x.GateNumber == 2)?.Score;
                    decimal? g3 = studentGateRows.FirstOrDefault(x => x.GateNumber == 3)?.Score;
                    decimal? weightedProcess20 = null;
                    var totalWeight = studentGateRows.Sum(x => x.GateWeightPercent);
                    if (studentGateRows.Length > 0 && totalWeight > 0)
                    {
                        var weighted = studentGateRows.Sum(x => x.Score * (x.GateWeightPercent / totalWeight));
                        weightedProcess20 = Math.Round(weighted * 0.20m, 2, MidpointRounding.AwayFromZero);
                    }

                    var latestRevision = studentRevisionsRaw.FirstOrDefault(r => r.ProjectId == p.Id && r.TargetKey == m.UserId);
                    var rootStudent = studentResultsRaw.SingleOrDefault(r => r.ProjectId == p.Id && r.StudentId == m.UserId);
                    StudentResultDto? resolvedResult = latestRevision is not null
                        ? PublishedResultResolver.Student(latestRevision)
                        : rootStudent is not null
                            ? PublishedResultResolver.Root(rootStudent)
                            : null;

                    var compSummary = resolvedResult?.Breakdown?.Components is { Count: > 0 } comps
                        ? string.Join("; ", comps.Select(c =>
                            $"{c.Name}({c.Stage ?? "DEF"}:{c.WeightPercent.ToString("0.##", CultureInfo.InvariantCulture)}%)={(c.Score.HasValue ? c.Score.Value.ToString("0.00", CultureInfo.InvariantCulture) : "-")}"))
                        : "-";

                    var relevantAppeals = appealStats.Where(a => a.ProjectId == p.Id && (a.TargetKey == 0 || a.TargetKey == m.UserId)).ToArray();

                    rows.Add(new ResultReconciliationExportRow(
                        p.Code,
                        p.Title,
                        p.TrackCode ?? "-",
                        p.Status,
                        m.StudentCode ?? "",
                        m.FullName,
                        m.Email,
                        m.MajorCode,
                        g1,
                        g2,
                        g3,
                        weightedProcess20,
                        compSummary,
                        resolvedResult?.Version,
                        resolvedResult?.TotalScore,
                        resolvedResult?.Outcome ?? "CHƯA CÔNG BỐ",
                        resolvedResult?.OutcomeDetail,
                        absenceSet.Contains((p.Id, m.UserId)),
                        relevantAppeals.Length,
                        relevantAppeals.Count(a => a.Status == "ACCEPTED"),
                        resolvedResult is null ? null : DateTime.SpecifyKind(resolvedResult.PublishedAt, DateTimeKind.Utc)));

                    if (rows.Count > MaxExportRows)
                        throw new ValidationException(new Dictionary<string, string[]> { ["export"] = ["Result reconciliation export exceeds 10000 rows; apply semester/major filters."] });
                }
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var (bytes, ext, contentType) = normalizedFormat == "csv"
            ? (ResultReconciliationWorkbook.CreateCsv(rows, now), "csv", "text/csv; charset=utf-8")
            : (ResultReconciliationWorkbook.CreateXlsx(rows), "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "DEPARTMENT_RESULTS_RECONCILIATION_EXPORTED",
            "DEPARTMENT",
            deptId,
            new Dictionary<string, object?>
            {
                ["format"] = normalizedFormat,
                ["semesterId"] = effectiveSemesterId,
                ["majorId"] = majorId,
                ["rowCount"] = rows.Count
            }), ct);

        return new RosterFile(bytes, $"department-results-reconciliation-{deptId}-{now:yyyyMMddHHmmss}.{ext}", contentType);
    }
}
