using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class SupervisorCandidateRepository(AipmsDbContext context) : ISupervisorCandidateRepository
{
    public async Task<SupervisorCandidateProject?> GetProjectAsync(long projectId, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var project = await context.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new SupervisorCandidateProject(p.Id, p.Team.AcademicSemesterId, p.Status,
                p.Team.AcademicSemester.Status == "ACTIVE" && p.Team.AcademicSemester.Organization.IsActive
                    && p.Team.AcademicSemester.StartDate <= today && today <= p.Team.AcademicSemester.EndDate,
                p.SupervisorAssignments.Any(a => a.EndedAt == null && a.IsPrimary), new List<long>(), null, null, null))
            .SingleOrDefaultAsync(ct);
        if (project is null) return null;
        var scope = await AIPMS.Infrastructure.Services.Projects.ProjectAcademicScopeReader.ReadAsync(context, projectId, ct);
        var majors = await context.Majors.AsNoTracking().Where(m => scope.MajorIds.Contains(m.Id)
                && m.IsActive && m.Department.IsActive && m.Department.Organization.IsActive
                && scope.DepartmentIds.Contains(m.DepartmentId))
            .Select(m => new SupervisorMajorScope(m.Id, m.DepartmentId, m.Code, m.Name)).ToListAsync(ct);
        if (scope.MajorDepartmentIds is not null)
            majors = majors.Where(m => scope.MajorDepartmentIds.TryGetValue(m.Id, out var departmentId) && m.DepartmentId == departmentId).ToList();
        var verified = !await context.TeamMembers.AnyAsync(m => context.Projects.Any(p => p.Id == projectId && p.TeamId == m.TeamId)
            && m.LeftAt == null && (m.User.Status != "ACTIVE" || m.User.AcademicProfileStatus != "VERIFIED"), ct);
        var occupied = await context.SupervisorAssignments.Where(a => a.ProjectId == projectId
            && a.EndedAt == null && a.AssignmentType == "DISCIPLINE_MENTOR")
            .Select(a => a.MajorId!.Value).ToListAsync(ct);
        return project with { DepartmentIds = verified && majors.Count == scope.MajorIds.Count ? scope.DepartmentIds : [],
            RequiredMajorIds = scope.MajorIds, MajorScopes = majors, OccupiedMentorMajors = occupied };
    }

    public async Task<IReadOnlyList<SupervisorSelectionPolicy>> GetSelectionPoliciesAsync(
        long academicSemesterId, DateTime now, CancellationToken ct, bool execution = false) =>
        await context.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == academicSemesterId && p.PeriodType == "SUPERVISOR_SELECTION"
                && (execution ? (p.Status == "ACTIVE" || p.Status == "CLOSED") && p.StartAt <= now
                    : p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt))
            .OrderByDescending(p => p.StartAt).ThenByDescending(p => p.Id).Take(execution ? 1 : 2)
            .Select(p => new SupervisorSelectionPolicy(p.Id, p.MaxProjectsPerSupervisor)).ToListAsync(ct);

    public async Task<PagedResult<SupervisorCandidateModel>> SearchAsync(SupervisorCandidateSearch search, CancellationToken ct)
    {
        var profiles = context.SupervisorProfiles.AsNoTracking()
            .Where(p => p.IsAvailable && p.User.Status == "ACTIVE" && p.User.Department != null
                && p.User.Department.IsActive && p.User.Department.Organization.IsActive
                && search.DepartmentIds.Contains(p.User.DepartmentId!.Value)
                && p.User.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)
                && !p.SupervisorRequests.Any(r => r.ProjectId == search.ProjectId && r.Status == "PENDING"
                    && r.AssignmentType == search.AssignmentType && r.MajorId == search.MajorId));
        if (!string.IsNullOrWhiteSpace(search.Search))
            profiles = profiles.Where(p => p.User.FullName.Contains(search.Search));
        if (!string.IsNullOrWhiteSpace(search.Expertise))
            profiles = profiles.Where(p => p.SupervisorExpertises.Any(e => e.ExpertiseName.Contains(search.Expertise)));
        if (search.AssignmentType == "DISCIPLINE_MENTOR" && search.MajorId is long majorId)
            profiles = profiles.Where(p => p.User.Department!.Majors.Any(m => m.Id == majorId && m.IsActive
                && p.SupervisorExpertises.Any(e => e.ExpertiseName == m.Code || e.ExpertiseName == m.Name)));

        // Count unended assignments across all semesters for the profile cap, and within
        // this semester for BE-12. Pending requests do not reserve a capacity slot.
        var candidates = profiles.Include(p => p.User).ThenInclude(u => u.Department)
            .Include(p => p.SupervisorExpertises)
            .Select(p => new
            {
                Profile = p,
                AlreadyAssigned = p.SupervisorAssignments.Any(a => a.EndedAt == null && a.ProjectId == search.ProjectId),
                Active = p.SupervisorAssignments.Where(a => a.EndedAt == null).Select(a => a.ProjectId).Distinct().Count(),
                SemesterActive = p.SupervisorAssignments.Where(a => a.EndedAt == null
                    && a.Project.Team.AcademicSemesterId == search.AcademicSemesterId)
                    .Select(a => a.ProjectId).Distinct().Count()
            })
            .Where(c => search.SemesterLimit > 0 && c.SemesterActive - (c.AlreadyAssigned ? 1 : 0) < search.SemesterLimit
                && (c.Profile.MaxActiveProjects == null || c.Active - (c.AlreadyAssigned ? 1 : 0) < c.Profile.MaxActiveProjects));

        var count = await candidates.LongCountAsync(ct);
        var items = await candidates.OrderBy(c => c.Profile.User.FullName).ThenBy(c => c.Profile.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(ct);
        return new(items.Select(c => new SupervisorCandidateModel(c.Profile.ToApplication(),
            c.Profile.MaxActiveProjects, c.Active, c.SemesterActive)).ToArray(), search.Page, search.PageSize, count);
    }
}
