using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Queries.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler(ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, AuthUserDto>
{
    public Task<AuthUserDto> Handle(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizedException();
        }

        return Task.FromResult(new AuthUserDto(
            currentUser.UserId.Value,
            currentUser.Email ?? string.Empty,
            currentUser.FullName ?? string.Empty,
            currentUser.Roles));
    }
}
