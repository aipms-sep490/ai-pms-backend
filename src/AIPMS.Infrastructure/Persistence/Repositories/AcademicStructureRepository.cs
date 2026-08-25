using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.Models;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Mappers;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DepartmentEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Department;
using MajorEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Major;
using OrganizationEntity = AIPMS.Infrastructure.Persistence.Generated.Models.Organization;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class AcademicStructureRepository(AipmsDbContext context)
    : IAcademicStructureRepository
{
    public async Task<PagedResult<AcademicOrganization>> GetOrganizationsAsync(
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Organizations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(organization =>
                organization.Code.Contains(search)
                || organization.Name.Contains(search));
        }

        if (isActive.HasValue)
        {
            query = query.Where(organization => organization.IsActive == isActive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderBy(static organization => organization.Code)
            .ThenBy(static organization => organization.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AcademicOrganization>(
            entities.Select(static organization => organization.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AcademicOrganization?> GetOrganizationAsync(
        long organizationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                organization => organization.Id == organizationId,
                cancellationToken);

        return entity?.ToApplication();
    }

    public Task<bool> OrganizationCodeOrNameExistsAsync(
        string code,
        string name,
        long? excludedOrganizationId,
        CancellationToken cancellationToken = default) =>
        context.Organizations.AsNoTracking().AnyAsync(
            organization =>
                (!excludedOrganizationId.HasValue
                 || organization.Id != excludedOrganizationId.Value)
                && (organization.Code == code || organization.Name == name),
            cancellationToken);

    public async Task<AcademicOrganization> CreateOrganizationAsync(
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = new OrganizationEntity
        {
            Code = code,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.Organizations.Add(entity);
        await SaveChangesAsync(cancellationToken);
        return entity.ToApplication();
    }

    public async Task<AcademicOrganization> UpdateOrganizationAsync(
        long organizationId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Organizations.SingleAsync(
            organization => organization.Id == organizationId,
            cancellationToken);

        entity.Code = code;
        entity.Name = name;
        entity.Description = description;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return entity.ToApplication();
    }

    public async Task<AcademicOrganization> SetOrganizationActiveAsync(
        long organizationId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Organizations
            .Include(static organization => organization.Departments)
            .ThenInclude(static department => department.Majors)
            .SingleAsync(
                organization => organization.Id == organizationId,
                cancellationToken);

        entity.IsActive = isActive;
        entity.UpdatedAt = utcNow;

        if (!isActive)
        {
            foreach (var department in entity.Departments)
            {
                department.IsActive = false;
                department.UpdatedAt = utcNow;

                foreach (var major in department.Majors)
                {
                    major.IsActive = false;
                    major.UpdatedAt = utcNow;
                }
            }
        }

        await SaveChangesAsync(cancellationToken);
        return entity.ToApplication();
    }

    public async Task<PagedResult<AcademicDepartment>> GetDepartmentsAsync(
        long? organizationId,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Departments
            .AsNoTracking()
            .Include(static department => department.Organization)
            .AsQueryable();

        if (organizationId.HasValue)
        {
            query = query.Where(department =>
                department.OrganizationId == organizationId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(department =>
                department.Code.Contains(search)
                || department.Name.Contains(search)
                || department.Organization.Code.Contains(search)
                || department.Organization.Name.Contains(search));
        }

        if (isActive.HasValue)
        {
            query = query.Where(department => department.IsActive == isActive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderBy(static department => department.Organization.Code)
            .ThenBy(static department => department.Code)
            .ThenBy(static department => department.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AcademicDepartment>(
            entities.Select(static department => department.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AcademicDepartment?> GetDepartmentAsync(
        long departmentId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Departments
            .AsNoTracking()
            .Include(static department => department.Organization)
            .SingleOrDefaultAsync(
                department => department.Id == departmentId,
                cancellationToken);

        return entity?.ToApplication();
    }

    public Task<bool> DepartmentCodeOrNameExistsAsync(
        long organizationId,
        string code,
        string name,
        long? excludedDepartmentId,
        CancellationToken cancellationToken = default) =>
        context.Departments.AsNoTracking().AnyAsync(
            department =>
                department.OrganizationId == organizationId
                && (!excludedDepartmentId.HasValue
                    || department.Id != excludedDepartmentId.Value)
                && (department.Code == code || department.Name == name),
            cancellationToken);

    public async Task<AcademicDepartment> CreateDepartmentAsync(
        long organizationId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = new DepartmentEntity
        {
            OrganizationId = organizationId,
            Code = code,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.Departments.Add(entity);
        await SaveChangesAsync(cancellationToken);
        return (await GetDepartmentAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicDepartment> UpdateDepartmentAsync(
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Departments.SingleAsync(
            department => department.Id == departmentId,
            cancellationToken);

        entity.Code = code;
        entity.Name = name;
        entity.Description = description;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetDepartmentAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicDepartment> SetDepartmentActiveAsync(
        long departmentId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Departments
            .Include(static department => department.Organization)
            .Include(static department => department.Majors)
            .SingleAsync(
                department => department.Id == departmentId,
                cancellationToken);

        entity.IsActive = isActive;
        entity.UpdatedAt = utcNow;

        if (!isActive)
        {
            foreach (var major in entity.Majors)
            {
                major.IsActive = false;
                major.UpdatedAt = utcNow;
            }
        }

        await SaveChangesAsync(cancellationToken);
        return entity.ToApplication();
    }

    public async Task<PagedResult<AcademicMajor>> GetMajorsAsync(
        long? organizationId,
        long? departmentId,
        string? search,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = context.Majors
            .AsNoTracking()
            .Include(static major => major.Department)
            .ThenInclude(static department => department.Organization)
            .AsQueryable();

        if (organizationId.HasValue)
        {
            query = query.Where(major =>
                major.Department.OrganizationId == organizationId.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(major => major.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(major =>
                major.Code.Contains(search)
                || major.Name.Contains(search)
                || major.Department.Code.Contains(search)
                || major.Department.Name.Contains(search));
        }

        if (isActive.HasValue)
        {
            query = query.Where(major => major.IsActive == isActive.Value);
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var entities = await query
            .OrderBy(static major => major.Department.Organization.Code)
            .ThenBy(static major => major.Department.Code)
            .ThenBy(static major => major.Code)
            .ThenBy(static major => major.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AcademicMajor>(
            entities.Select(static major => major.ToApplication()).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<AcademicMajor?> GetMajorAsync(
        long majorId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Majors
            .AsNoTracking()
            .Include(static major => major.Department)
            .ThenInclude(static department => department.Organization)
            .SingleOrDefaultAsync(major => major.Id == majorId, cancellationToken);

        return entity?.ToApplication();
    }

    public Task<bool> MajorCodeOrNameExistsAsync(
        long departmentId,
        string code,
        string name,
        long? excludedMajorId,
        CancellationToken cancellationToken = default) =>
        context.Majors.AsNoTracking().AnyAsync(
            major =>
                major.DepartmentId == departmentId
                && (!excludedMajorId.HasValue || major.Id != excludedMajorId.Value)
                && (major.Code == code || major.Name == name),
            cancellationToken);

    public async Task<AcademicMajor> CreateMajorAsync(
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = new MajorEntity
        {
            DepartmentId = departmentId,
            Code = code,
            Name = name,
            Description = description,
            IsActive = true,
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        context.Majors.Add(entity);
        await SaveChangesAsync(cancellationToken);
        return (await GetMajorAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicMajor> UpdateMajorAsync(
        long majorId,
        long departmentId,
        string code,
        string name,
        string? description,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Majors.SingleAsync(
            major => major.Id == majorId,
            cancellationToken);

        entity.DepartmentId = departmentId;
        entity.Code = code;
        entity.Name = name;
        entity.Description = description;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetMajorAsync(entity.Id, cancellationToken))!;
    }

    public async Task<AcademicMajor> SetMajorActiveAsync(
        long majorId,
        bool isActive,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.Majors.SingleAsync(
            major => major.Id == majorId,
            cancellationToken);

        entity.IsActive = isActive;
        entity.UpdatedAt = utcNow;

        await SaveChangesAsync(cancellationToken);
        return (await GetMajorAsync(entity.Id, cancellationToken))!;
    }

    public async Task<IReadOnlyList<AcademicHierarchyOrganization>> GetHierarchyAsync(
        long? organizationId,
        string? search,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        var query = context.Organizations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(static organization => organization.Departments)
            .ThenInclude(static department => department.Majors)
            .AsQueryable();

        if (organizationId.HasValue)
        {
            query = query.Where(organization => organization.Id == organizationId.Value);
        }

        if (!includeInactive)
        {
            query = query.Where(static organization => organization.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(organization =>
                organization.Code.Contains(search)
                || organization.Name.Contains(search)
                || organization.Departments.Any(department =>
                    department.Code.Contains(search)
                    || department.Name.Contains(search)
                    || department.Majors.Any(major =>
                        major.Code.Contains(search) || major.Name.Contains(search))));
        }

        var entities = await query
            .OrderBy(static organization => organization.Code)
            .ThenBy(static organization => organization.Id)
            .ToListAsync(cancellationToken);

        return entities
            .Select(organization => organization.ToHierarchy(includeInactive))
            .ToArray();
    }

    public Task<AcademicUserScope?> GetUserScopeAsync(
        long userId,
        CancellationToken cancellationToken = default) =>
        context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.DepartmentId.HasValue)
            .Select(user => new AcademicUserScope(
                user.Department!.OrganizationId,
                user.DepartmentId!.Value))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "An academic structure record with the same scoped code already exists.");
        }
    }
}
