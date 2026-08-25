using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AccountSecurity.Abstractions;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Services;
using MediatR;

namespace AIPMS.Application.Features.AccountSecurity.Queries;

public sealed record GetUsersQuery(
    string? Search,
    string? Status,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<UserAccountDto>>;

public sealed class GetUsersQueryHandler(
    IUserAccountRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetUsersQuery, PagedResult<UserAccountDto>>
{
    public async Task<PagedResult<UserAccountDto>> Handle(
        GetUsersQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var result = await repository.GetUsersAsync(
            request.Search?.Trim(),
            request.Status?.Trim().ToUpperInvariant(),
            request.Page,
            request.PageSize,
            cancellationToken);

        return new PagedResult<UserAccountDto>(
            result.Items.Select(static user => user.ToDto()).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }
}

public sealed record GetUserByIdQuery(long UserId) : IRequest<UserAccountDto>;

public sealed class GetUserByIdQueryHandler(
    IUserAccountRepository repository,
    AccountSecurityAccessService accessService)
    : IRequestHandler<GetUserByIdQuery, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        accessService.EnsureAdministrator();
        var user = await repository.GetUserAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);
        return user.ToDto();
    }
}

public sealed record GetMyProfileQuery : IRequest<UserAccountDto>;

public sealed class GetMyProfileQueryHandler(
    IUserAccountRepository repository,
    ICurrentUser currentUser) : IRequestHandler<GetMyProfileQuery, UserAccountDto>
{
    public async Task<UserAccountDto> Handle(
        GetMyProfileQuery request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var user = await repository.GetUserAsync(userId, cancellationToken)
            ?? throw new UnauthorizedException();
        return user.ToDto();
    }
}
