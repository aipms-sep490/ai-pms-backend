using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.Abstractions;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Domain.Entities;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using DbModels = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Persistence.Repositories;

public sealed class SupervisorRepository(AipmsDbContext dbContext) : ISupervisorRepository
{
    public async Task<IReadOnlyList<SupervisorCandidateDto>> GetEligibleCandidatesAsync(
        string? expertise,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SupervisorProfiles.AsNoTracking().Where(p => p.IsAvailable);
        if (!string.IsNullOrWhiteSpace(expertise))
        {
            var needle = expertise.ToLower();
            query = query.Where(p => p.SupervisorExpertises.Any(e => e.ExpertiseName.ToLower().Contains(needle)));
        }

        return await query
            .Select(p => new
            {
                p.Id,
                p.User.FullName,
                p.MaxActiveProjects,
                Active = p.SupervisorAssignments.Count(a => a.EndedAt == null),
                Expertises = p.SupervisorExpertises
                    .OrderBy(e => e.ExpertiseName)
                    .Select(e => new SupervisorExpertiseDto(e.ExpertiseName, e.ProficiencyLevel))
                    .ToList()
            })
            .Where(p => !p.MaxActiveProjects.HasValue || p.Active < p.MaxActiveProjects.Value)
            .OrderBy(p => p.FullName)
            .Select(p => new SupervisorCandidateDto(
                p.Id, p.FullName, p.Active, p.MaxActiveProjects,
                p.MaxActiveProjects.HasValue ? p.MaxActiveProjects.Value - p.Active : null,
                p.Expertises,
                false, null))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SupervisorDto>> GetPagedSupervisorsAsync(
        int pageNumber,
        int pageSize,
        string? search,
        bool? isAvailable,
        string? expertise,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SupervisorProfiles
            .Include(p => p.User)
            .Include(p => p.SupervisorExpertises)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(p => p.User.FullName.ToLower().Contains(searchLower)
                                  || p.User.Email.ToLower().Contains(searchLower)
                                  || (p.User.EmployeeCode != null && p.User.EmployeeCode.ToLower().Contains(searchLower)));
        }

        if (isAvailable.HasValue)
        {
            query = query.Where(p => p.IsAvailable == isAvailable.Value);
        }

        if (!string.IsNullOrWhiteSpace(expertise))
        {
            var expertiseLower = expertise.ToLower();
            query = query.Where(p => p.SupervisorExpertises.Any(e => e.ExpertiseName.ToLower().Contains(expertiseLower)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var dbItems = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = dbItems.Select(p => new SupervisorDto(
            p.Id,
            p.UserId,
            p.User.FullName,
            p.User.Email,
            p.User.Title,
            p.Bio,
            p.MaxActiveProjects,
            p.IsAvailable
        )).ToList();

        return new PagedResult<SupervisorDto>(items, pageNumber, pageSize, totalCount);
    }

    public async Task<SupervisorDetailDto?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var p = await dbContext.SupervisorProfiles
            .Include(x => x.User)
            .Include(x => x.SupervisorExpertises)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (p == null) return null;

        var expertises = p.SupervisorExpertises.Select(e => new SupervisorExpertiseDto(
            e.ExpertiseName,
            e.ProficiencyLevel
        )).ToList();

        return new SupervisorDetailDto(
            p.Id,
            p.UserId,
            p.User.FullName,
            p.User.Email,
            p.User.Phone,
            p.User.EmployeeCode,
            p.User.Title,
            p.Bio,
            p.MaxActiveProjects,
            p.IsAvailable,
            expertises
        );
    }

    public async Task<SupervisorProfile?> GetProfileByIdAsync(long id, CancellationToken cancellationToken)
    {
        var p = await dbContext.SupervisorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (p == null) return null;

        return new SupervisorProfile
        {
            Id = p.Id,
            UserId = p.UserId,
            Bio = p.Bio,
            MaxActiveProjects = p.MaxActiveProjects,
            IsAvailable = p.IsAvailable,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    public async Task<SupervisorProfile?> GetProfileByUserIdAsync(long userId, CancellationToken cancellationToken)
    {
        var p = await dbContext.SupervisorProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (p == null) return null;

        return new SupervisorProfile
        {
            Id = p.Id,
            UserId = p.UserId,
            Bio = p.Bio,
            MaxActiveProjects = p.MaxActiveProjects,
            IsAvailable = p.IsAvailable,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    public async Task<SupervisorProfile?> GetProfileByUserIdForUpdateAsync(long userId, CancellationToken cancellationToken)
    {
        var p = await dbContext.SupervisorProfiles
            .FromSqlInterpolated($"SELECT * FROM dbo.supervisor_profiles WITH (UPDLOCK, HOLDLOCK) WHERE user_id = {userId}")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return p == null ? null : new SupervisorProfile
        {
            Id = p.Id,
            UserId = p.UserId,
            Bio = p.Bio,
            MaxActiveProjects = p.MaxActiveProjects,
            IsAvailable = p.IsAvailable,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    public async Task UpdateProfileAsync(SupervisorProfile profile, CancellationToken cancellationToken)
    {
        var dbProfile = await dbContext.SupervisorProfiles
            .FirstOrDefaultAsync(x => x.Id == profile.Id, cancellationToken);

        if (dbProfile == null) return;

        dbProfile.Bio = profile.Bio;
        dbProfile.MaxActiveProjects = profile.MaxActiveProjects;
        dbProfile.IsAvailable = profile.IsAvailable;
        dbProfile.UpdatedAt = DateTime.UtcNow;

        dbContext.SupervisorProfiles.Update(dbProfile);
    }

    public async Task UpdateExpertisesAsync(
        long supervisorProfileId,
        IEnumerable<SupervisorExpertise> expertises,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SupervisorExpertises
            .Where(e => e.SupervisorProfileId == supervisorProfileId)
            .ToListAsync(cancellationToken);

        dbContext.SupervisorExpertises.RemoveRange(existing);

        var newDbExpertises = expertises.Select(e => new DbModels.SupervisorExpertise
        {
            SupervisorProfileId = supervisorProfileId,
            ExpertiseName = e.ExpertiseName,
            ProficiencyLevel = e.ProficiencyLevel,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        await dbContext.SupervisorExpertises.AddRangeAsync(newDbExpertises, cancellationToken);
    }

    public async Task<bool> ExistsAsync(long id, CancellationToken cancellationToken)
    {
        return await dbContext.SupervisorProfiles
            .AnyAsync(x => x.Id == id, cancellationToken);
    }
}
