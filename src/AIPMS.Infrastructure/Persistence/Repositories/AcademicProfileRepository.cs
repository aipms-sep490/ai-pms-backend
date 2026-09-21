using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class AcademicProfileRepository(AipmsDbContext db) : IAcademicProfileRepository
{
    private IQueryable<AcademicProfileDto> Query() => db.Users.AsNoTracking().Select(u => new AcademicProfileDto(
        u.Id, u.FullName, u.Email, u.StudentCode, u.DepartmentId, u.Department == null ? null : u.Department.Name,
        u.MajorId, u.Major == null ? null : u.Major.Name, u.AcademicProfileStatus ?? "PENDING",
        u.AcademicProfileReviewedBy, u.AcademicProfileReviewedAt, u.AcademicProfileRejectionReason));

    public Task<AcademicProfileDto?> GetAsync(long userId, CancellationToken ct = default)
        => Query().SingleOrDefaultAsync(x => x.UserId == userId, ct);

    public async Task<PagedResult<AcademicProfileDto>> SearchAsync(string? status, long? departmentId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Query();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim().ToUpperInvariant());
        if (departmentId.HasValue) query = query.Where(x => x.DepartmentId == departmentId.Value);
        var count = await query.LongCountAsync(ct);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.UserId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items, page, pageSize, count);
    }

    public async Task<AcademicProfileDto> SetStatusAsync(long userId, string status, long reviewerId, string? reason, DateTime now, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new AIPMS.Application.Common.Exceptions.NotFoundException("User", userId);
        user.AcademicProfileStatus = status;
        user.AcademicProfileReviewedBy = reviewerId;
        user.AcademicProfileReviewedAt = now;
        user.AcademicProfileRejectionReason = reason;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(userId, ct))!;
    }

    public Task<bool> IsVerifiedAsync(long userId, CancellationToken ct = default)
        => db.Users.AsNoTracking().AnyAsync(x => x.Id == userId && x.AcademicProfileStatus == "VERIFIED", ct);
}
