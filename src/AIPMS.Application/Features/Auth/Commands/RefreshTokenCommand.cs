using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResponse>;
