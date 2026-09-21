using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Academic.DTOs;

namespace AIPMS.Application.Features.Academic.Abstractions;

public interface IAcademicProfileRepository
{
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<long?> GetReviewerDepartmentAsync(long actorId, CancellationToken ct);
    Task LockAsync(long userId, CancellationToken ct);
    Task<AcademicProfileDto?> GetAsync(long userId, CancellationToken ct = default);
    Task<PagedResult<AcademicProfileDto>> SearchAsync(string? status, long? departmentId, int page, int pageSize, CancellationToken ct = default);
    Task<AcademicProfileDto> SetStatusAsync(long userId, string status, long reviewerId, string? reason, DateTime now, CancellationToken ct = default);
    Task<bool> IsVerifiedAsync(long userId, CancellationToken ct = default);
}
