using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest;
