using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<AuthUserDto>;
