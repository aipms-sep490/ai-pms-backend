using AIPMS.Application.Common.Models;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Academic.Abstractions;
using AIPMS.Application.Features.Academic.DTOs;
using AIPMS.Application.Features.Academic.Services;
using MediatR;

namespace AIPMS.Application.Features.Academic.Queries;

public sealed record GetMyAcademicProfileQuery : IRequest<AcademicProfileDto>;
public sealed record GetAcademicProfilesQuery(string? Status, long? DepartmentId, int Page, int PageSize) : IRequest<PagedResult<AcademicProfileDto>>;

public sealed class GetMyAcademicProfileHandler(IAcademicProfileRepository repository, ICurrentUser currentUser)
    : IRequestHandler<GetMyAcademicProfileQuery, AcademicProfileDto>
{
    public async Task<AcademicProfileDto> Handle(GetMyAcademicProfileQuery request, CancellationToken ct)
        => await repository.GetAsync(currentUser.UserId ?? throw new AIPMS.Application.Common.Exceptions.UnauthorizedException(), ct)
           ?? throw new AIPMS.Application.Common.Exceptions.NotFoundException("AcademicProfile", currentUser.UserId!.Value);
}

public sealed class GetAcademicProfilesHandler(IAcademicProfileRepository repository, AcademicAccessService access)
    : IRequestHandler<GetAcademicProfilesQuery, PagedResult<AcademicProfileDto>>
{
    public Task<PagedResult<AcademicProfileDto>> Handle(GetAcademicProfilesQuery request, CancellationToken ct)
    {
        var managedDepartment = await access.GetManagedDepartmentIdAsync(ct);
        if (managedDepartment.HasValue && request.DepartmentId.HasValue && request.DepartmentId != managedDepartment)
            throw new AIPMS.Application.Common.Exceptions.ForbiddenException("Department staff can only view their assigned department.");
        return await repository.SearchAsync(request.Status, managedDepartment ?? request.DepartmentId, request.Page, request.PageSize, ct);
    }
}
