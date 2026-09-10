using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed record LogoutCommand(string RefreshToken) : IRequest;
