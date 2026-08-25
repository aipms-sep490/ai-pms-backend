using AIPMS.Application.Features.Auth.Commands.ChangePassword;
using AIPMS.Application.Features.Auth.Commands.ForgotPassword;
using AIPMS.Application.Features.Auth.Commands.Logout;
using AIPMS.Application.Features.Auth.Commands.RefreshToken;
using AIPMS.Application.Features.Auth.Commands.ResetPassword;
using FluentValidation;

namespace AIPMS.Application.Features.Auth.Validators;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() =>
        RuleFor(static command => command.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() =>
        RuleFor(static command => command.RefreshToken).NotEmpty().MaximumLength(512);
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(static command => command.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(static command => command.NewPassword).StrongPassword();
    }
}

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() =>
        RuleFor(static command => command.Email).NotEmpty().EmailAddress().MaximumLength(320);
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(static command => command.Token).NotEmpty().MaximumLength(512);
        RuleFor(static command => command.NewPassword).StrongPassword();
    }
}

internal static class PasswordValidationRules
{
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(
        this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(10)
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a special character.");
}
