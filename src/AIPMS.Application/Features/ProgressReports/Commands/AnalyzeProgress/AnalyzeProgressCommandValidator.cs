using FluentValidation;

namespace AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;

public sealed class AnalyzeProgressCommandValidator : AbstractValidator<AnalyzeProgressCommand>
{
    public AnalyzeProgressCommandValidator()
    {
        RuleFor(command => command.TotalTasks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Total tasks cannot be negative.");

        RuleFor(command => command.OverdueTasks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Overdue tasks cannot be negative.")
            .LessThanOrEqualTo(command => command.TotalTasks)
            .WithMessage("Overdue tasks cannot exceed total tasks.");

        RuleFor(command => command.BlockedTasks)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Blocked tasks cannot be negative.")
            .LessThanOrEqualTo(command => command.TotalTasks)
            .WithMessage("Blocked tasks cannot exceed total tasks.");

        RuleFor(command => command.MilestoneCompletionRate)
            .InclusiveBetween(0m, 1m)
            .WithMessage("Milestone completion rate must be between 0 and 1.");
    }
}
