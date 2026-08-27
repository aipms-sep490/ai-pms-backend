using AIPMS.Application.Features.AccountSecurity.Commands;
using AIPMS.Application.Features.AccountSecurity.DTOs;
using AIPMS.Application.Features.AccountSecurity.Queries;
using AIPMS.Application.Features.Auth.Validators;
using FluentValidation;

namespace AIPMS.Application.Features.AccountSecurity.Validators;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        this.AddPagingRules(static query => query.Page, static query => query.PageSize);
        RuleFor(static query => query.Search).MaximumLength(255);
        RuleFor(static query => query.Status)
            .Must(BeUserStatus)
            .When(static query => !string.IsNullOrWhiteSpace(query.Status))
            .WithMessage("Status must be ACTIVE, INACTIVE or SUSPENDED.");
    }

    private static bool BeUserStatus(string? status) =>
        status is not null
        && new[] { "ACTIVE", "INACTIVE", "SUSPENDED" }
            .Contains(status.Trim().ToUpperInvariant(), StringComparer.Ordinal);
}

public sealed class GetUserByIdQueryValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdQueryValidator() => RuleFor(static query => query.UserId).GreaterThan(0);
}

public sealed class CreateUserAccountCommandValidator : AbstractValidator<CreateUserAccountCommand>
{
    public CreateUserAccountCommandValidator()
    {
        RuleFor(static command => command.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(static command => command.Password).StrongPassword();
        RuleFor(static command => command.FullName).NotEmpty().MaximumLength(255);
        RuleFor(static command => command.Phone).MaximumLength(30);
        RuleFor(static command => command.StudentCode).MaximumLength(50);
        RuleFor(static command => command.EmployeeCode).MaximumLength(50);
        RuleFor(static command => command.Title).MaximumLength(100);
        RuleFor(static command => command.DepartmentId).GreaterThan(0).When(static command => command.DepartmentId.HasValue);
        RuleFor(static command => command.MajorId).GreaterThan(0).When(static command => command.MajorId.HasValue);
        RuleFor(static command => command.RoleIds)
            .NotNull()
            .Must(static roleIds => roleIds.Count > 0)
            .WithMessage("At least one role is required.")
            .Must(static roleIds => roleIds.All(static id => id > 0))
            .WithMessage("Every role id must be greater than zero.");
    }
}

public sealed class UpdateMyProfileCommandValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileCommandValidator()
    {
        RuleFor(static command => command.FullName).NotEmpty().MaximumLength(255);
        RuleFor(static command => command.Phone).MaximumLength(30);
        RuleFor(static command => command.Title).MaximumLength(100);
    }
}

public sealed class ImportUserAccountsCommandValidator
    : AbstractValidator<ImportUserAccountsCommand>
{
    public ImportUserAccountsCommandValidator()
    {
        RuleFor(static command => command.Accounts)
            .NotNull()
            .Must(static accounts => accounts.Count is >= 1 and <= 500)
            .WithMessage("An import must contain between 1 and 500 accounts.");
        RuleForEach(static command => command.Accounts)
            .SetValidator(new CreateUserAccountRequestValidator());
    }
}

internal sealed class CreateUserAccountRequestValidator
    : AbstractValidator<CreateUserAccountRequest>
{
    public CreateUserAccountRequestValidator()
    {
        RuleFor(static request => request.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(static request => request.Password).StrongPassword();
        RuleFor(static request => request.FullName).NotEmpty().MaximumLength(255);
        RuleFor(static request => request.Phone).MaximumLength(30);
        RuleFor(static request => request.StudentCode).MaximumLength(50);
        RuleFor(static request => request.EmployeeCode).MaximumLength(50);
        RuleFor(static request => request.Title).MaximumLength(100);
        RuleFor(static request => request.DepartmentId).GreaterThan(0).When(static request => request.DepartmentId.HasValue);
        RuleFor(static request => request.MajorId).GreaterThan(0).When(static request => request.MajorId.HasValue);
        RuleFor(static request => request.RoleIds)
            .NotNull()
            .Must(static roleIds => roleIds.Count > 0 && roleIds.All(static id => id > 0));
    }
}

public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(static command => command.UserId).GreaterThan(0);
        RuleFor(static command => command.Status)
            .NotEmpty()
            .Must(static status => new[] { "ACTIVE", "INACTIVE", "SUSPENDED" }
                .Contains(status.Trim().ToUpperInvariant(), StringComparer.Ordinal))
            .WithMessage("Status must be ACTIVE, INACTIVE or SUSPENDED.");
    }
}

public sealed class AssignUserRoleCommandValidator : AbstractValidator<AssignUserRoleCommand>
{
    public AssignUserRoleCommandValidator()
    {
        RuleFor(static command => command.UserId).GreaterThan(0);
        RuleFor(static command => command.RoleId).GreaterThan(0);
    }
}

public sealed class RemoveUserRoleCommandValidator : AbstractValidator<RemoveUserRoleCommand>
{
    public RemoveUserRoleCommandValidator()
    {
        RuleFor(static command => command.UserId).GreaterThan(0);
        RuleFor(static command => command.RoleId).GreaterThan(0);
    }
}

public sealed class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator()
    {
        this.AddPagingRules(static query => query.Page, static query => query.PageSize);
        RuleFor(static query => query.Search).MaximumLength(255);
    }
}

public sealed class GetRoleByIdQueryValidator : AbstractValidator<GetRoleByIdQuery>
{
    public GetRoleByIdQueryValidator() => RuleFor(static query => query.RoleId).GreaterThan(0);
}

public sealed class GetPermissionsQueryValidator : AbstractValidator<GetPermissionsQuery>
{
    public GetPermissionsQueryValidator()
    {
        this.AddPagingRules(static query => query.Page, static query => query.PageSize);
        RuleFor(static query => query.Search).MaximumLength(255);
    }
}

public sealed class GetPermissionByIdQueryValidator : AbstractValidator<GetPermissionByIdQuery>
{
    public GetPermissionByIdQueryValidator() =>
        RuleFor(static query => query.PermissionId).GreaterThan(0);
}

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator() => AddCatalogRules(50);

    private void AddCatalogRules(int codeLength)
    {
        RuleFor(static command => command.Code).SecurityCode(codeLength);
        RuleFor(static command => command.Name).NotEmpty().MaximumLength(150);
        RuleFor(static command => command.Description).MaximumLength(500);
    }
}

public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(static command => command.RoleId).GreaterThan(0);
        RuleFor(static command => command.Code).SecurityCode(50);
        RuleFor(static command => command.Name).NotEmpty().MaximumLength(150);
        RuleFor(static command => command.Description).MaximumLength(500);
    }
}

public sealed class DeleteRoleCommandValidator : AbstractValidator<DeleteRoleCommand>
{
    public DeleteRoleCommandValidator() => RuleFor(static command => command.RoleId).GreaterThan(0);
}

public sealed class CreatePermissionCommandValidator : AbstractValidator<CreatePermissionCommand>
{
    public CreatePermissionCommandValidator()
    {
        RuleFor(static command => command.Code).SecurityCode(100);
        RuleFor(static command => command.Name).NotEmpty().MaximumLength(150);
        RuleFor(static command => command.Description).MaximumLength(500);
    }
}

public sealed class UpdatePermissionCommandValidator : AbstractValidator<UpdatePermissionCommand>
{
    public UpdatePermissionCommandValidator()
    {
        RuleFor(static command => command.PermissionId).GreaterThan(0);
        RuleFor(static command => command.Code).SecurityCode(100);
        RuleFor(static command => command.Name).NotEmpty().MaximumLength(150);
        RuleFor(static command => command.Description).MaximumLength(500);
    }
}

public sealed class DeletePermissionCommandValidator : AbstractValidator<DeletePermissionCommand>
{
    public DeletePermissionCommandValidator() => RuleFor(static command => command.PermissionId).GreaterThan(0);
}

public sealed class ReplaceRolePermissionsCommandValidator
    : AbstractValidator<ReplaceRolePermissionsCommand>
{
    public ReplaceRolePermissionsCommandValidator()
    {
        RuleFor(static command => command.RoleId).GreaterThan(0);
        RuleFor(static command => command.PermissionIds)
            .NotNull()
            .Must(static ids => ids.Count <= 500)
            .WithMessage("A role cannot receive more than 500 permissions.")
            .Must(static ids => ids.All(static id => id > 0))
            .WithMessage("Every permission id must be greater than zero.");
    }
}

public sealed class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    public GetAuditLogsQueryValidator()
    {
        this.AddPagingRules(static query => query.Page, static query => query.PageSize);
        RuleFor(static query => query.ActorUserId).GreaterThan(0).When(static query => query.ActorUserId.HasValue);
        RuleFor(static query => query.Action).MaximumLength(100);
        RuleFor(static query => query.EntityType).MaximumLength(100);
        RuleFor(static query => query.Outcome)
            .Must(static value => value is not null
                && new[] { "SUCCESS", "FAILURE", "DENIED" }
                    .Contains(value.Trim().ToUpperInvariant(), StringComparer.Ordinal))
            .When(static query => !string.IsNullOrWhiteSpace(query.Outcome));
        RuleFor(static query => query)
            .Must(static query => !query.FromUtc.HasValue
                || !query.ToUtc.HasValue
                || query.FromUtc <= query.ToUtc)
            .WithMessage("FromUtc must be before or equal to ToUtc.");
    }
}

internal static class AccountSecurityValidationRules
{
    public static IRuleBuilderOptions<T, string> SecurityCode<T>(
        this IRuleBuilder<T, string> rule,
        int maximumLength) =>
        rule.NotEmpty()
            .MaximumLength(maximumLength)
            .Matches("^[A-Za-z][A-Za-z0-9_.:-]*$")
            .WithMessage("Code must start with a letter and contain only letters, numbers, dots, colons, underscores or hyphens.");

    public static void AddPagingRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, int>> page,
        System.Linq.Expressions.Expression<Func<T, int>> pageSize)
    {
        validator.RuleFor(page).GreaterThanOrEqualTo(1);
        validator.RuleFor(pageSize).InclusiveBetween(1, 100);
    }
}
