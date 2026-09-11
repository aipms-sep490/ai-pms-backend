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
        return await context.Projects.AsNoTracking().Where(p => p.Id == projectId)
            .Select(p => new SupervisorCandidateProject(p.Id, p.Team.AcademicSemesterId, p.Status,
                p.Team.AcademicSemester.Status == "ACTIVE" && p.Team.AcademicSemester.Organization.IsActive
                    && p.Team.AcademicSemester.StartDate <= today && today <= p.Team.AcademicSemester.EndDate,
                context.SupervisorAssignments.Any(a => a.ProjectId == p.Id && a.EndedAt == null),
                p.ProjectMajors.Where(m => m.Major.IsActive && m.Major.Department.IsActive
                    && m.Major.Department.OrganizationId == p.Team.AcademicSemester.OrganizationId)
                    .Select(m => m.Major.DepartmentId).Distinct().ToList()))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SupervisorSelectionPolicy>> GetSelectionPoliciesAsync(
        long academicSemesterId, DateTime now, CancellationToken ct) =>
        await context.ProjectPeriods.AsNoTracking()
            .Where(p => p.AcademicSemesterId == academicSemesterId && p.PeriodType == "SUPERVISOR_SELECTION"
                && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt)
            .OrderBy(p => p.Id).Take(2)
            .Select(p => new SupervisorSelectionPolicy(p.Id, p.MaxProjectsPerSupervisor)).ToListAsync(ct);

    public async Task<PagedResult<SupervisorCandidateModel>> SearchAsync(SupervisorCandidateSearch search, CancellationToken ct)
    {
        var profiles = context.SupervisorProfiles.AsNoTracking()
            .Where(p => p.IsAvailable && p.User.Status == "ACTIVE" && p.User.Department != null
                && p.User.Department.IsActive && p.User.Department.Organization.IsActive
                && search.DepartmentIds.Contains(p.User.DepartmentId!.Value)
                && p.User.UserRoleUsers.Any(r => r.Role.Code == AppRoles.Lecturer)
                && !p.SupervisorRequests.Any(r => r.ProjectId == search.ProjectId && r.Status == "PENDING"));
        if (!string.IsNullOrWhiteSpace(search.Search))
            profiles = profiles.Where(p => p.User.FullName.Contains(search.Search));
        if (!string.IsNullOrWhiteSpace(search.Expertise))
            profiles = profiles.Where(p => p.SupervisorExpertises.Any(e => e.ExpertiseName.Contains(search.Expertise)));

        // Count unended assignments across all semesters for the profile cap, and within
        // this semester for BE-12. Pending requests do not reserve a capacity slot.
        var candidates = profiles.Include(p => p.User).ThenInclude(u => u.Department)
            .Include(p => p.SupervisorExpertises)
            .Select(p => new
            {
                Profile = p,
                Active = p.SupervisorAssignments.Where(a => a.EndedAt == null).Select(a => a.ProjectId).Distinct().Count(),
                SemesterActive = p.SupervisorAssignments.Where(a => a.EndedAt == null
                    && a.Project.Team.AcademicSemesterId == search.AcademicSemesterId)
                    .Select(a => a.ProjectId).Distinct().Count()
            })
            .Where(c => search.SemesterLimit > 0 && c.SemesterActive < search.SemesterLimit
                && (c.Profile.MaxActiveProjects == null || c.Active < c.Profile.MaxActiveProjects));

        var count = await candidates.LongCountAsync(ct);
        var items = await candidates.OrderBy(c => c.Profile.User.FullName).ThenBy(c => c.Profile.Id)
            .Skip((search.Page - 1) * search.PageSize).Take(search.PageSize).ToListAsync(ct);
        return new(items.Select(c => new SupervisorCandidateModel(c.Profile.ToApplication(),
            c.Profile.MaxActiveProjects, c.Active, c.SemesterActive)).ToArray(), search.Page, search.PageSize, count);
    }
}
