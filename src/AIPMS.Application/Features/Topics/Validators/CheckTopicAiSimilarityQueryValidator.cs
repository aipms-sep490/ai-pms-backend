using AIPMS.Application.Features.Topics.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Topics.Validators;

public sealed class CheckTopicAiSimilarityQueryValidator : AbstractValidator<CheckTopicAiSimilarityQuery>
{
    public CheckTopicAiSimilarityQueryValidator()
    {
        RuleFor(x => x.Request).NotNull();
        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.TeamId).GreaterThan(0);
            RuleFor(x => x.Request.ProjectId).GreaterThan(0).When(x => x.Request.ProjectId.HasValue);
            RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(300)
                .WithMessage("Nhập tên đề tài tối đa 300 ký tự để kiểm tra ý tưởng.");
            RuleFor(x => x.Request.ProblemStatement).MaximumLength(4000);
            RuleFor(x => x.Request.Objectives).MaximumLength(4000);
            RuleFor(x => x.Request.ExpectedOutput).MaximumLength(4000);
            RuleFor(x => x.Request.Domain).MaximumLength(200);
            RuleFor(x => x.Request.Keywords).NotNull().Must(x => x is null || x.Count <= 20);
            RuleForEach(x => x.Request.Keywords).NotEmpty().MaximumLength(100);
        });
    }
}
