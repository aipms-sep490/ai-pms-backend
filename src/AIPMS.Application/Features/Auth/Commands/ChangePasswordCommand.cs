using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed record ChangePasswordCommand(
    string CurrentPassword,
    string NewPassword) : IRequest;
