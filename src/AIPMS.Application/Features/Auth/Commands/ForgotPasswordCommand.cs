using AIPMS.Application.Features.Auth.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Auth.Commands;

public sealed record ForgotPasswordCommand(string Email) : IRequest<MessageResponse>;
