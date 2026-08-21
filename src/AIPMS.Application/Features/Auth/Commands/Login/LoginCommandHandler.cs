using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(
    IAuthRepository authRepository,
    IPasswordHashingService passwordHashingService,
    IAccessTokenService accessTokenService,
    TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var account = await authRepository.FindByEmailAsync(
            request.Email.Trim(),
            cancellationToken);

        if (account is null
            || !passwordHashingService.Verify(account.PasswordHash, request.Password))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!string.Equals(account.Status, "ACTIVE", StringComparison.Ordinal))
        {
            throw new ForbiddenException("This account is not active.");
        }

        var token = accessTokenService.Create(new AccessTokenDescriptor(
            account.Id,
            account.Email,
            account.FullName,
            account.Roles));

        await authRepository.UpdateLastLoginAsync(
            account.Id,
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        return new LoginResponse(
            token.Token,
            "Bearer",
            token.ExpiresAtUtc,
            new AuthUserDto(
                account.Id,
                account.Email,
                account.FullName,
                account.Roles));
    }
}
