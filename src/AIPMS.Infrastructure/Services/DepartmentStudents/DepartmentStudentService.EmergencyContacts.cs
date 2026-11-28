using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Features.DepartmentStudents;
using AIPMS.Application.Features.StudentRosters;
using AIPMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.DepartmentStudents;

internal sealed partial class DepartmentStudentService
{
    public async Task<RosterFile> ExportEmergencyContactsAsync(
        DepartmentStudentQuery query,
        CancellationToken ct = default)
    {
        var (actorId, deptId) = await RequireDepartmentStaffAsync(ct);
        var (filtered, _) = await LoadDepartmentStudentRowsAsync(deptId, query, ct);
        if (filtered.Count > MaxExportRows)
            throw new DomainException("ROSTER_EXPORT_ROW_LIMIT_EXCEEDED: narrow filters to at most 10000 students.");

        var projectIds = filtered
            .Where(r => r.ProjectId.HasValue)
            .Select(r => r.ProjectId!.Value)
            .Distinct()
            .ToArray();

        var supervisorByProject = projectIds.Length == 0
            ? new Dictionary<long, (string FullName, string Email, string? Phone)>()
            : (await db.SupervisorAssignments.AsNoTracking()
                .Where(a => projectIds.Contains(a.ProjectId) && a.EndedAt == null && a.IsPrimary)
                .OrderBy(a => a.AssignedAt)
                .Select(a => new
                {
                    a.ProjectId,
                    a.SupervisorProfile.User.FullName,
                    a.SupervisorProfile.User.Email,
                    a.SupervisorProfile.User.Phone
                })
                .ToListAsync(ct))
                .GroupBy(a => a.ProjectId)
                .ToDictionary(g => g.Key, g => (g.First().FullName, g.First().Email, g.First().Phone));

        var exportRows = filtered.Select(s =>
        {
            var sup = s.ProjectId.HasValue && supervisorByProject.TryGetValue(s.ProjectId.Value, out var found)
                ? found
                : (FullName: "", Email: "", Phone: (string?)null);

            return new EmergencyContactExportRow(
                s.StudentCode,
                s.FullName,
                s.Email,
                s.Phone,
                s.ClassCode,
                s.MajorName ?? s.MajorCode,
                s.TeamCode,
                s.ProjectCode,
                s.ProjectStatus,
                sup.FullName,
                sup.Email,
                sup.Phone);
        }).ToArray();

        var bytes = DepartmentStudentWorkbook.CreateEmergencyContactsWorkbook(exportRows);
        await audit.RecordAsync(new AuditEntry(
            actorId,
            "DEPARTMENT_EMERGENCY_CONTACTS_EXPORTED",
            "DEPARTMENT",
            deptId,
            new Dictionary<string, object?> { ["rowCount"] = exportRows.Length, ["semesterId"] = query.SemesterId }), ct);

        return new RosterFile(
            bytes,
            $"department-emergency-contacts-{deptId}-{Now:yyyyMMdd}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }
}
