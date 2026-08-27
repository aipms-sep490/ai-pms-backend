using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ProgressMeetings.Abstractions;
using AIPMS.Application.Features.ProgressMeetings.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.ProgressMeetings.Commands;

public sealed record CreateMeetingCommand(long ProjectId, string Title, string? Agenda, DateTime StartAt,
    DateTime? EndAt, string? Location, string? OnlineUrl, IReadOnlyCollection<long> ParticipantIds) : IRequest<MeetingDto>;
public sealed record UpdateMeetingCommand(long Id, string Title, string? Agenda, DateTime StartAt,
    DateTime? EndAt, string? Location, string? OnlineUrl) : IRequest<Unit>;
public sealed record CancelMeetingCommand(long Id) : IRequest<Unit>;
public sealed record UpdateMeetingMinutesCommand(long Id, string? MeetingNotes, bool Complete) : IRequest<Unit>;
public sealed record SetMeetingAttendanceCommand(long Id, long UserId, string Status) : IRequest<Unit>;
public sealed record ReplaceMeetingParticipantsCommand(long Id, IReadOnlyCollection<long> ParticipantIds) : IRequest<Unit>;
public sealed record AddMeetingFeedbackCommand(long Id, string FeedbackText) : IRequest<Unit>;
public sealed record AddMeetingDecisionCommand(long Id, string Content) : IRequest<Unit>;
public sealed record AddMeetingBlockerCommand(long Id, string Content) : IRequest<Unit>;
public sealed record CreateMeetingActionItemCommand(long Id, string Title, string? Description, long OwnerUserId,
    DateOnly? DueDate, string Status, long? TaskId, long? MilestoneId) : IRequest<Unit>;
public sealed record UpdateMeetingActionItemStatusCommand(long Id, long ActionItemId, string Status) : IRequest<Unit>;

public sealed class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0); RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.EndAt).GreaterThanOrEqualTo(x => x.StartAt).When(x => x.EndAt.HasValue);
        RuleFor(x => x.OnlineUrl).MaximumLength(1000); RuleFor(x => x.Location).MaximumLength(500);
        RuleFor(x => x.ParticipantIds).NotNull().Must(x => x.Distinct().Count() == x.Count).WithMessage("Participant IDs must be unique.");
        RuleForEach(x => x.ParticipantIds).GreaterThan(0);
    }
}
public sealed class UpdateMeetingValidator : AbstractValidator<UpdateMeetingCommand>
{ public UpdateMeetingValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Title).NotEmpty().MaximumLength(255); RuleFor(x => x.EndAt).GreaterThanOrEqualTo(x => x.StartAt).When(x => x.EndAt.HasValue); } }
public sealed class CancelMeetingValidator : AbstractValidator<CancelMeetingCommand>
{ public CancelMeetingValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class UpdateMeetingMinutesValidator : AbstractValidator<UpdateMeetingMinutesCommand>
{ public UpdateMeetingMinutesValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class SetMeetingAttendanceValidator : AbstractValidator<SetMeetingAttendanceCommand>
{ public SetMeetingAttendanceValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.UserId).GreaterThan(0); RuleFor(x => x.Status).Must(x => new[] { "INVITED", "ACCEPTED", "DECLINED", "ATTENDED", "ABSENT" }.Contains(x.ToUpperInvariant())); } }
public sealed class ReplaceMeetingParticipantsValidator : AbstractValidator<ReplaceMeetingParticipantsCommand>
{ public ReplaceMeetingParticipantsValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.ParticipantIds).NotNull().Must(x => x.Distinct().Count() == x.Count); RuleForEach(x => x.ParticipantIds).GreaterThan(0); } }
public sealed class CreateMeetingActionItemValidator : AbstractValidator<CreateMeetingActionItemCommand>
{ public CreateMeetingActionItemValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Title).NotEmpty().MaximumLength(255); RuleFor(x => x.OwnerUserId).GreaterThan(0); RuleFor(x => x.Status).NotEmpty().Must(x => x is not null && ActionItemStatuses.Contains(x.ToUpperInvariant())); } internal static readonly string[] ActionItemStatuses = ["TODO", "IN_PROGRESS", "BLOCKED", "DONE", "CANCELLED"]; }
public sealed class AddMeetingDecisionValidator : AbstractValidator<AddMeetingDecisionCommand>
{ public AddMeetingDecisionValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Content).NotEmpty(); } }
public sealed class AddMeetingBlockerValidator : AbstractValidator<AddMeetingBlockerCommand>
{ public AddMeetingBlockerValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.Content).NotEmpty(); } }
public sealed class AddMeetingFeedbackValidator : AbstractValidator<AddMeetingFeedbackCommand>
{ public AddMeetingFeedbackValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.FeedbackText).NotEmpty(); } }
public sealed class UpdateMeetingActionItemStatusValidator : AbstractValidator<UpdateMeetingActionItemStatusCommand>
{ public UpdateMeetingActionItemStatusValidator() { RuleFor(x => x.Id).GreaterThan(0); RuleFor(x => x.ActionItemId).GreaterThan(0); RuleFor(x => x.Status).NotEmpty().Must(x => x is not null && CreateMeetingActionItemValidator.ActionItemStatuses.Contains(x.ToUpperInvariant())); } }

public sealed class CreateMeetingHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<CreateMeetingCommand, MeetingDto>
{
    public async Task<MeetingDto> Handle(CreateMeetingCommand request, CancellationToken ct)
    {
        var actor = ProgressMeetingAccess.UserId(user);
        if (!await repository.ProjectExistsAsync(request.ProjectId, ct)) throw new NotFoundException("Project", request.ProjectId);
        await ProgressMeetingAccess.EnsureScheduler(actor, request.ProjectId, repository, ct);
        var participants = request.ParticipantIds.Distinct().ToArray();
        if (!await repository.AreValidParticipantsAsync(request.ProjectId, participants, ct)) throw new ForbiddenException("Every participant must be an active project member or assigned supervisor.");
        var id = await repository.CreateMeetingAsync(new(request.ProjectId, actor, request.Title.Trim(), request.Agenda,
            request.StartAt, request.EndAt, request.Location, request.OnlineUrl, participants), ct);
        return await repository.GetMeetingAsync(id, ct) ?? throw new NotFoundException("Meeting", id);
    }
}
public sealed class UpdateMeetingHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<UpdateMeetingCommand, Unit>
{
    public async Task<Unit> Handle(UpdateMeetingCommand request, CancellationToken ct)
    {
        var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id);
        await ProgressMeetingAccess.EnsureScheduler(ProgressMeetingAccess.UserId(user), meeting.ProjectId, repository, ct);
        if (meeting.Status != "SCHEDULED") throw new ConflictException("Only scheduled meetings can be updated.");
        if (!await repository.UpdateScheduledMeetingAsync(request.Id, new(request.Title.Trim(), request.Agenda, request.StartAt, request.EndAt, request.Location, request.OnlineUrl), ct)) throw new ConflictException("The meeting was modified concurrently.");
        return Unit.Value;
    }
}
public sealed class CancelMeetingHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<CancelMeetingCommand, Unit>
{
    public async Task<Unit> Handle(CancelMeetingCommand request, CancellationToken ct) { var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id); await ProgressMeetingAccess.EnsureScheduler(ProgressMeetingAccess.UserId(user), meeting.ProjectId, repository, ct); if (!await repository.CancelMeetingAsync(request.Id, ct)) throw new ConflictException("Only scheduled meetings can be cancelled."); return Unit.Value; }
}
public sealed class UpdateMeetingMinutesHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<UpdateMeetingMinutesCommand, Unit>
{
    public async Task<Unit> Handle(UpdateMeetingMinutesCommand request, CancellationToken ct) { var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id); await ProgressMeetingAccess.EnsureScheduler(ProgressMeetingAccess.UserId(user), meeting.ProjectId, repository, ct); if (!await repository.UpdateMinutesAsync(request.Id, request.MeetingNotes, request.Complete, ct)) throw new ConflictException("Cancelled meetings cannot be changed."); return Unit.Value; }
}
public sealed class SetMeetingAttendanceHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<SetMeetingAttendanceCommand, Unit>
{
    public async Task<Unit> Handle(SetMeetingAttendanceCommand request, CancellationToken ct) { var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id); await ProgressMeetingAccess.EnsureScheduler(ProgressMeetingAccess.UserId(user), meeting.ProjectId, repository, ct); if (!await repository.SetAttendanceAsync(request.Id, request.UserId, request.Status.ToUpperInvariant(), ct)) throw new NotFoundException("MeetingParticipant", request.UserId); return Unit.Value; }
}
public sealed class AddMeetingFeedbackHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<AddMeetingFeedbackCommand, Unit>
{
    public async Task<Unit> Handle(AddMeetingFeedbackCommand request, CancellationToken ct) { var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id); var assignment = await repository.GetActiveSupervisorAssignmentAsync(ProgressMeetingAccess.UserId(user), meeting.ProjectId, ct) ?? throw new ForbiddenException("Only an assigned supervisor can add feedback."); await repository.AddMeetingFeedbackAsync(meeting.Id, meeting.ProjectId, assignment, request.FeedbackText.Trim(), ct); return Unit.Value; }
}
public sealed class ReplaceMeetingParticipantsHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<ReplaceMeetingParticipantsCommand, Unit>
{
    public async Task<Unit> Handle(ReplaceMeetingParticipantsCommand request, CancellationToken ct)
    {
        var meeting = await repository.GetMeetingAsync(request.Id, ct) ?? throw new NotFoundException("Meeting", request.Id);
        var actor = ProgressMeetingAccess.UserId(user); await ProgressMeetingAccess.EnsureScheduler(actor, meeting.ProjectId, repository, ct);
        if (meeting.Status != "SCHEDULED") throw new ConflictException("Only scheduled meetings can change participants.");
        var ids = request.ParticipantIds.Distinct().ToArray();
        if (!await repository.AreValidParticipantsAsync(meeting.ProjectId, ids, ct)) throw new ForbiddenException("Every participant must be in project scope.");
        await repository.ReplaceParticipantsAsync(meeting.Id, actor, meeting.Title, ids, ct); return Unit.Value;
    }
}
public sealed class AddMeetingDecisionHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<AddMeetingDecisionCommand, Unit>
{ public async Task<Unit> Handle(AddMeetingDecisionCommand r, CancellationToken ct) { var m = await repository.GetMeetingAsync(r.Id, ct) ?? throw new NotFoundException("Meeting", r.Id); var actor = ProgressMeetingAccess.UserId(user); await ProgressMeetingAccess.EnsureProjectAccess(actor, m.ProjectId, repository, ct); if (m.Status == "CANCELLED") throw new ConflictException("Cancelled meetings cannot be changed."); await repository.AddDecisionAsync(m.Id, actor, r.Content.Trim(), ct); return Unit.Value; } }
public sealed class AddMeetingBlockerHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<AddMeetingBlockerCommand, Unit>
{ public async Task<Unit> Handle(AddMeetingBlockerCommand r, CancellationToken ct) { var m = await repository.GetMeetingAsync(r.Id, ct) ?? throw new NotFoundException("Meeting", r.Id); var actor = ProgressMeetingAccess.UserId(user); await ProgressMeetingAccess.EnsureProjectAccess(actor, m.ProjectId, repository, ct); if (m.Status == "CANCELLED") throw new ConflictException("Cancelled meetings cannot be changed."); await repository.AddBlockerAsync(m.Id, actor, r.Content.Trim(), ct); return Unit.Value; } }
public sealed class CreateMeetingActionItemHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<CreateMeetingActionItemCommand, Unit>
{
    public async Task<Unit> Handle(CreateMeetingActionItemCommand r, CancellationToken ct)
    {
        var m = await repository.GetMeetingAsync(r.Id, ct) ?? throw new NotFoundException("Meeting", r.Id); var actor = ProgressMeetingAccess.UserId(user);
        await ProgressMeetingAccess.EnsureScheduler(actor, m.ProjectId, repository, ct); if (m.Status == "CANCELLED") throw new ConflictException("Cancelled meetings cannot be changed.");
        if (!await repository.IsProjectMemberAsync(r.OwnerUserId, m.ProjectId, ct)) throw new ForbiddenException("Action item owner must be an active project member.");
        if (r.TaskId.HasValue && !await repository.IsTaskInProjectAsync(r.TaskId.Value, m.ProjectId, ct)) throw new ConflictException("Task is not in the meeting project.");
        if (r.MilestoneId.HasValue && !await repository.IsMilestoneInProjectAsync(r.MilestoneId.Value, m.ProjectId, ct)) throw new ConflictException("Milestone is not in the meeting project.");
        await repository.AddActionItemAsync(new(m.Id, actor, r.Title.Trim(), r.Description, r.OwnerUserId, r.DueDate, r.Status.ToUpperInvariant(), r.TaskId, r.MilestoneId), ct); return Unit.Value;
    }
}
public sealed class UpdateMeetingActionItemStatusHandler(ICurrentUser user, IProgressMeetingRepository repository) : IRequestHandler<UpdateMeetingActionItemStatusCommand, Unit>
{ public async Task<Unit> Handle(UpdateMeetingActionItemStatusCommand r, CancellationToken ct) { var m = await repository.GetMeetingAsync(r.Id, ct) ?? throw new NotFoundException("Meeting", r.Id); await ProgressMeetingAccess.EnsureScheduler(ProgressMeetingAccess.UserId(user), m.ProjectId, repository, ct); if (!await repository.UpdateActionItemStatusAsync(m.Id, r.ActionItemId, r.Status.ToUpperInvariant(), ct)) throw new NotFoundException("MeetingActionItem", r.ActionItemId); return Unit.Value; } }
