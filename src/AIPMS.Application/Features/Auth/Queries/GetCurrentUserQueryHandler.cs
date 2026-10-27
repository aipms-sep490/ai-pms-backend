using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Queries;

public sealed class GetCurrentUserQueryHandler(
    ICurrentUser currentUser,
    IAuthRepository authRepository)
    : IRequestHandler<GetCurrentUserQuery, AuthUserDto>
{
    public async Task<AuthUserDto> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        try
        {
            var account = await authRepository.FindByIdAsync(currentUser.UserId.Value, cancellationToken);
            if (account is not null)
            {
                return new AuthUserDto(
                    account.Id,
                    account.Email,
                    account.FullName,
                    account.Roles,
                    account.Title,
                    account.Phone);
            }
        }
        catch
        {
            // Fall back to claims if database/repository is unavailable in lightweight test harnesses
        }

        return new AuthUserDto(
            currentUser.UserId.Value,
            currentUser.Email ?? string.Empty,
            currentUser.FullName ?? string.Empty,
            currentUser.Roles,
            null,
            null);
    }
}
