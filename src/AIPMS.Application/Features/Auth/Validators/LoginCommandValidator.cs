using AIPMS.Application.Features.Auth.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Auth.Validators;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(static command => command.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(static command => command.Password)
            .NotEmpty()
            .MaximumLength(256);
    }
}
