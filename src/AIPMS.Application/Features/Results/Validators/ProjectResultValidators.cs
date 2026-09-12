using AIPMS.Application.Features.Results.Commands;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Results.Validators;

public sealed class ConfigureResultPolicyRequestValidator : AbstractValidator<ConfigureResultPolicyRequest>
{
    public ConfigureResultPolicyRequestValidator()
    {
        RuleFor(r => r.PassThreshold).InclusiveBetween(0m, 10m).Must(v => decimal.Round(v, 2) == v);
        RuleFor(r => r.ConcurrencyToken).Must(t => t is null || Guid.TryParse(t, out _));
        RuleFor(r => r.Assignments).NotNull().Must(a => a is { Count: > 0 and <= 100 }
            && a.All(i => i is not null && i.AssignmentId > 0 && i.WeightPercent > 0 && i.WeightPercent <= 100
                && decimal.Round(i.WeightPercent, 2) == i.WeightPercent)
            && a.Select(i => i.AssignmentId).Distinct().Count() == a.Count && a.Sum(i => i.WeightPercent) == 100m);
    }
}
public sealed class ConfigureResultPolicyCommandValidator : AbstractValidator<ConfigureResultPolicyCommand>
{
    public ConfigureResultPolicyCommandValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Input).NotNull().SetValidator(new ConfigureResultPolicyRequestValidator());
    }
}
public sealed class PublishProjectResultCommandValidator : AbstractValidator<PublishProjectResultCommand>
{
    public PublishProjectResultCommandValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Input).NotNull();
        When(r => r.Input is not null, () => RuleFor(r => r.Input.ConfirmationToken).NotEmpty().Matches("\\A[0-9a-fA-F]{64}\\z"));
    }
}
public sealed class GetResultPolicyQueryValidator : AbstractValidator<GetResultPolicyQuery>
{
    public GetResultPolicyQueryValidator() => RuleFor(r => r.ProjectId).GreaterThan(0);
}
public sealed class PreviewProjectResultQueryValidator : AbstractValidator<PreviewProjectResultQuery>
{
    public PreviewProjectResultQueryValidator() => RuleFor(r => r.ProjectId).GreaterThan(0);
}
public sealed class GetProjectResultQueryValidator : AbstractValidator<GetProjectResultQuery>
{
    public GetProjectResultQueryValidator() => RuleFor(r => r.ProjectId).GreaterThan(0);
}
