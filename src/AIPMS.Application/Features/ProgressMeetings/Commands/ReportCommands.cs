using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.ProgressMeetings.Commands;

public sealed record CreateProgressReportCommand(long ProjectId, long ReportPeriodId, string Summary,
    string Completed, string InProgress, string Blockers, string Risks, string NextActions)
    : IRequest<ProgressReportDto>;
public sealed record UpdateProgressReportCommand(long Id, string Summary, string Completed,
    string InProgress, string Blockers, string Risks, string NextActions) : IRequest<Unit>;
public sealed record SubmitProgressReportCommand(long Id) : IRequest<Unit>;
public sealed record AddProgressReportFeedbackCommand(long Id, string FeedbackText) : IRequest<Unit>;
public sealed record AddProgressReportContributionCommand(long Id, string SectionType, string Content) : IRequest<Unit>;

public sealed class CreateProgressReportValidator : AbstractValidator<CreateProgressReportCommand>
{
    public CreateProgressReportValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.ReportPeriodId).GreaterThan(0);
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.Completed).NotNull(); RuleFor(x => x.InProgress).NotNull(); RuleFor(x => x.Blockers).NotNull(); RuleFor(x => x.Risks).NotNull(); RuleFor(x => x.NextActions).NotNull();
    }
}
public sealed class UpdateProgressReportValidator : AbstractValidator<UpdateProgressReportCommand>
{ public UpdateProgressReportValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Summary).NotEmpty(); RuleFor(x => x.Completed).NotNull(); RuleFor(x => x.InProgress).NotNull(); RuleFor(x => x.Blockers).NotNull(); RuleFor(x => x.Risks).NotNull(); RuleFor(x => x.NextActions).NotNull(); } }
public sealed class SubmitProgressReportValidator : AbstractValidator<SubmitProgressReportCommand>
{ public SubmitProgressReportValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class AddProgressReportFeedbackValidator : AbstractValidator<AddProgressReportFeedbackCommand>
{ public AddProgressReportFeedbackValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.FeedbackText).NotEmpty(); } }
public sealed class AddProgressReportContributionValidator : AbstractValidator<AddProgressReportContributionCommand>
{ public AddProgressReportContributionValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Content).NotEmpty(); RuleFor(x => x.SectionType).NotEmpty().Must(x => x is not null && ReportSections.Types.Contains(x.ToUpperInvariant())); } }

internal static class ReportSections
{
    internal static readonly string[] Types = ["COMPLETED", "IN_PROGRESS", "BLOCKERS", "RISKS", "NEXT_ACTIONS"];
    internal static IReadOnlyDictionary<string, string> From(string completed, string inProgress, string blockers, string risks, string nextActions) =>
        new Dictionary<string, string> { ["COMPLETED"] = completed, ["IN_PROGRESS"] = inProgress, ["BLOCKERS"] = blockers, ["RISKS"] = risks, ["NEXT_ACTIONS"] = nextActions };
}

public sealed class CreateProgressReportHandler(ICurrentUser user, IProgressMeetingRepository repository)
    : IRequestHandler<CreateProgressReportCommand, ProgressReportDto>
{
    public async Task<ProgressReportDto> Handle(CreateProgressReportCommand request, CancellationToken ct)
    {
        var userId = ProgressMeetingAccess.UserId(user);
        if (!await repository.ProjectExistsAsync(request.ProjectId, ct)) throw new NotFoundException("Project", request.ProjectId);
        if (!await repository.IsProjectMemberAsync(userId, request.ProjectId, ct)) throw new ForbiddenException();
        var period = await repository.GetReportPeriodAsync(request.ReportPeriodId, ct) ?? throw new NotFoundException("ProgressReportPeriod", request.ReportPeriodId);
        if (period.ProjectId != request.ProjectId || period.Status != "OPEN") throw new ConflictException("The report period is not open for this project.");
        var id = await repository.CreateReportAsync(new(request.ProjectId, userId, period.Id, request.Summary.Trim(),
            ReportSections.From(request.Completed, request.InProgress, request.Blockers, request.Risks, request.NextActions)), ct);
        return await repository.GetReportAsync(id, ct) ?? throw new NotFoundException("ProgressReport", id);
    }
}
public sealed class UpdateProgressReportHandler(ICurrentUser user, IProgressMeetingRepository repository)
    : IRequestHandler<UpdateProgressReportCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProgressReportCommand request, CancellationToken ct)
    {
        var report = await repository.GetReportAsync(request.Id, ct) ?? throw new NotFoundException("ProgressReport", request.Id);
        if (!await repository.IsProjectMemberAsync(ProgressMeetingAccess.UserId(user), report.ProjectId, ct)) throw new ForbiddenException();
        if (report.Status != "DRAFT") throw new ConflictException("Submitted reports are immutable.");
        if (!await repository.UpdateDraftReportAsync(request.Id, new(request.Summary.Trim(), ReportSections.From(request.Completed, request.InProgress, request.Blockers, request.Risks, request.NextActions)), ct))
            throw new ConflictException("The report was modified concurrently.");
        return Unit.Value;
    }
}
public sealed class SubmitProgressReportHandler(ICurrentUser user, IProgressMeetingRepository repository, TimeProvider clock)
    : IRequestHandler<SubmitProgressReportCommand, Unit>
{
    public async Task<Unit> Handle(SubmitProgressReportCommand request, CancellationToken ct)
    {
        var report = await repository.GetReportAsync(request.Id, ct) ?? throw new NotFoundException("ProgressReport", request.Id);
        var userId = ProgressMeetingAccess.UserId(user);
        if (!await repository.IsProjectLeaderAsync(userId, report.ProjectId, ct)) throw new ForbiddenException("Only the project leader can submit reports.");
        if (report.Status != "DRAFT") throw new ConflictException("The report has already been submitted.");
        if (report.DeadlineAt is null || report.LatePolicy is null) throw new ConflictException("This report has no configured database deadline policy.");
        if (clock.GetUtcNow().UtcDateTime > report.DeadlineAt && report.LatePolicy == "BLOCK") throw new ConflictException("The report deadline has passed.");
        if (!await repository.SubmitDraftReportAsync(request.Id, clock.GetUtcNow().UtcDateTime, ct)) throw new ConflictException("The report was modified concurrently.");
        return Unit.Value;
    }
}
public sealed class AddProgressReportContributionHandler(ICurrentUser user, IProgressMeetingRepository repository)
    : IRequestHandler<AddProgressReportContributionCommand, Unit>
{
    public async Task<Unit> Handle(AddProgressReportContributionCommand request, CancellationToken ct)
    {
        var report = await repository.GetReportAsync(request.Id, ct) ?? throw new NotFoundException("ProgressReport", request.Id);
        var actor = ProgressMeetingAccess.UserId(user);
        if (!await repository.IsProjectMemberAsync(actor, report.ProjectId, ct)) throw new ForbiddenException();
        if (report.Status != "DRAFT") throw new ConflictException("Submitted reports are immutable.");
        await repository.AddContributionAsync(report.Id, actor, request.SectionType.ToUpperInvariant(), request.Content.Trim(), ct); return Unit.Value;
    }
}
public sealed class AddProgressReportFeedbackHandler(ICurrentUser user, IProgressMeetingRepository repository)
    : IRequestHandler<AddProgressReportFeedbackCommand, Unit>
{
    public async Task<Unit> Handle(AddProgressReportFeedbackCommand request, CancellationToken ct)
    {
        var report = await repository.GetReportAsync(request.Id, ct) ?? throw new NotFoundException("ProgressReport", request.Id);
        if (report.Status == "DRAFT") throw new ConflictException("Draft reports cannot be reviewed.");
        var assignment = await repository.GetActiveSupervisorAssignmentAsync(ProgressMeetingAccess.UserId(user), report.ProjectId, ct)
            ?? throw new ForbiddenException("Only an assigned supervisor can review this report.");
        await repository.AddReportFeedbackAsync(report.Id, report.ProjectId, assignment, request.FeedbackText.Trim(), ct);
        return Unit.Value;
    }
}
